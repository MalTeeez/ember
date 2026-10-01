using System.Runtime.InteropServices;
using Ember.Core.Net;

namespace Ember.Native;

/// <summary>One process from NtQuerySystemInformation(SystemProcessInformation).</summary>
internal readonly record struct ProcSample(int Pid, int ParentPid, string Name, long CreateTime, long CpuTime, ulong PrivateBytes, ulong IoBytes);

internal static class SysInfo
{
    // ───────── Processes ─────────

    private const int SystemProcessInformation = 5;
    private const uint STATUS_INFO_LENGTH_MISMATCH = 0xC0000004;
    private static IntPtr _procBuf;
    private static int _procBufSize;

    /// <summary>
    /// All processes in one call. Offsets are from SYSTEM_PROCESS_INFORMATION (x64).
    /// IoBytes = ReadTransferCount + WriteTransferCount (the same data as PDH "IO Data Bytes").
    /// </summary>
    public static List<ProcSample>? Processes()
    {
        if (_procBuf == IntPtr.Zero)
        {
            _procBufSize = 512 * 1024;
            _procBuf = Marshal.AllocHGlobal(_procBufSize);
        }
        for (int attempt = 0; attempt < 5; attempt++)
        {
            int st = W.NtQuerySystemInformation(SystemProcessInformation, _procBuf, _procBufSize, out int needed);
            if ((uint)st == STATUS_INFO_LENGTH_MISMATCH)
            {
                Marshal.FreeHGlobal(_procBuf);
                _procBufSize = Math.Max(needed, _procBufSize) + 64 * 1024;
                _procBuf = Marshal.AllocHGlobal(_procBufSize);
                continue;
            }
            if (st != 0) return null;
            var list = new List<ProcSample>(400);
            int offset = 0;
            while (true)
            {
                IntPtr p = _procBuf + offset;
                int next = Marshal.ReadInt32(p);
                long create = Marshal.ReadInt64(p + 32);
                long user = Marshal.ReadInt64(p + 40);
                long kernel = Marshal.ReadInt64(p + 48);
                ushort nameLen = (ushort)Marshal.ReadInt16(p + 56);
                IntPtr nameBuf = Marshal.ReadIntPtr(p + 64);
                int pid = (int)(long)Marshal.ReadIntPtr(p + 80);
                int ppid = (int)(long)Marshal.ReadIntPtr(p + 88);
                ulong priv = (ulong)(long)Marshal.ReadIntPtr(p + 184); // PagefileUsage = private bytes
                ulong read = (ulong)Marshal.ReadInt64(p + 232);
                ulong write = (ulong)Marshal.ReadInt64(p + 240);
                string name = pid == 0 ? "Idle" : nameBuf == IntPtr.Zero ? "System" : Marshal.PtrToStringUni(nameBuf, nameLen / 2);
                list.Add(new ProcSample(pid, ppid, name, create, user + kernel, priv, read + write));
                if (next == 0) break;
                offset += next;
            }
            return list;
        }
        return null;
    }

    // ───────── CPU sets (core efficiency class) ─────────

    /// <summary>
    /// Efficiency class and physical core index for each (group, logical processor index). Empty on failure.
    /// CoreIndex is the same for both threads of one physical core.
    /// </summary>
    public static Dictionary<(int Group, int Index), (int Efficiency, int Core)> CpuSets()
    {
        var map = new Dictionary<(int, int), (int, int)>();
        try
        {
            W.GetSystemCpuSetInformation(IntPtr.Zero, 0, out uint len, IntPtr.Zero, 0);
            if (len == 0) return map;
            IntPtr buf = Marshal.AllocHGlobal((int)len);
            try
            {
                if (!W.GetSystemCpuSetInformation(buf, len, out len, IntPtr.Zero, 0)) return map;
                int off = 0;
                while (off < len)
                {
                    IntPtr e = buf + off;
                    int size = Marshal.ReadInt32(e);
                    int type = Marshal.ReadInt32(e + 4);
                    if (size <= 0) break;
                    if (type == 0) // CpuSetInformation
                    {
                        int group = (ushort)Marshal.ReadInt16(e + 12);
                        int lp = Marshal.ReadByte(e + 14);
                        int core = Marshal.ReadByte(e + 15);
                        int eff = Marshal.ReadByte(e + 18);
                        map[(group, lp)] = (eff, group * 256 + core);
                    }
                    off += size;
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch (EntryPointNotFoundException) { }
        return map;
    }

    // ───────── Memory ─────────

    public static bool Memory(out ulong avail, out ulong total)
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!W.GlobalMemoryStatusEx(ref m)) { avail = total = 0; return false; }
        avail = m.ullAvailPhys;
        total = m.ullTotalPhys;
        return true;
    }

    public static bool Commit(out ulong commit, out ulong limit)
    {
        var p = new PERFORMANCE_INFORMATION { cb = (uint)Marshal.SizeOf<PERFORMANCE_INFORMATION>() };
        if (!W.GetPerformanceInfo(ref p, p.cb)) { commit = limit = 0; return false; }
        ulong page = (ulong)p.PageSize;
        commit = (ulong)p.CommitTotal * page;
        limit = (ulong)p.CommitLimit * page;
        return true;
    }

    // ───────── Volumes ─────────

    public static List<(string Root, ulong Free, ulong Total)> FixedVolumes()
    {
        var list = new List<(string, ulong, ulong)>();
        uint old = W.SetErrorMode(W.SEM_FAILCRITICALERRORS);
        try
        {
            uint mask = W.GetLogicalDrives();
            for (int i = 0; i < 26; i++)
            {
                if ((mask & (1u << i)) == 0) continue;
                string root = (char)('A' + i) + ":\\";
                if (W.GetDriveType(root) != W.DRIVE_FIXED) continue;
                if (W.GetDiskFreeSpaceEx(root, out ulong freeCaller, out ulong total, out _) && total > 0)
                    list.Add((root, freeCaller, total));
            }
        }
        finally { W.SetErrorMode(old); }
        return list;
    }

    // ───────── Network ─────────

    [DllImport("iphlpapi.dll")] private static extern int GetIfTable2(out IntPtr table);
    [DllImport("iphlpapi.dll")] private static extern void FreeMibTable(IntPtr table);
    private static byte[] _ifBuf = Array.Empty<byte>();

    /// <summary>All interface rows (GetIfTable2), or null on failure.</summary>
    public static List<IfRow>? Interfaces()
    {
        if (GetIfTable2(out IntPtr table) != 0 || table == IntPtr.Zero) return null;
        try
        {
            int n = Marshal.ReadInt32(table);
            int size = IfTableLayout.TableHeader + n * IfTableLayout.RowSize;
            if (_ifBuf.Length < size) _ifBuf = new byte[size];
            Marshal.Copy(table, _ifBuf, 0, size);
            return IfTableLayout.ParseTable(_ifBuf.AsSpan(0, size));
        }
        finally { FreeMibTable(table); }
    }
}

/// <summary>Process details and actions (PID based).</summary>
internal static class ProcessInfo
{
    private const int ProcessCommandLineInformation = 60;

    public static string? ImagePath(int pid)
    {
        IntPtr h = W.OpenProcess(W.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var buf = new char[32768];
            int size = buf.Length;
            return W.QueryFullProcessImageName(h, 0, buf, ref size) ? new string(buf, 0, size) : null;
        }
        finally { W.CloseHandle(h); }
    }

    public static string? Name(int pid)
    {
        var path = ImagePath(pid);
        return path == null ? null : Path.GetFileName(path);
    }

    /// <summary>Command line (Windows 8.1+, ProcessCommandLineInformation).</summary>
    public static string? CommandLine(int pid)
    {
        IntPtr h = W.OpenProcess(W.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            W.NtQueryInformationProcess(h, ProcessCommandLineInformation, IntPtr.Zero, 0, out int len);
            if (len <= 0 || len > 1 << 20) return null;
            IntPtr buf = Marshal.AllocHGlobal(len);
            try
            {
                if (W.NtQueryInformationProcess(h, ProcessCommandLineInformation, buf, len, out _) != 0) return null;
                // UNICODE_STRING: USHORT Length, USHORT MaximumLength, (pad), PWSTR Buffer at 8.
                ushort l = (ushort)Marshal.ReadInt16(buf);
                IntPtr s = Marshal.ReadIntPtr(buf + 8);
                return s == IntPtr.Zero ? null : Marshal.PtrToStringUni(s, l / 2);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        finally { W.CloseHandle(h); }
    }

    public static DateTime? StartTime(int pid)
    {
        IntPtr h = W.OpenProcess(W.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            if (!W.GetProcessTimes(h, out var create, out _, out _, out _)) return null;
            return DateTime.FromFileTime(create.Value);
        }
        finally { W.CloseHandle(h); }
    }

    public static bool Kill(int pid)
    {
        IntPtr h = W.OpenProcess(W.PROCESS_TERMINATE, false, pid);
        if (h == IntPtr.Zero) return false;
        try { return W.TerminateProcess(h, 1); }
        finally { W.CloseHandle(h); }
    }
}
