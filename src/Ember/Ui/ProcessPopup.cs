using System.Diagnostics;
using System.Globalization;
using System.Text;
using Ember.App;
using Ember.Core.Render;
using Ember.Core.Util;
using Ember.Native;
using Ember.Sensors;

namespace Ember.Ui;

/// <summary>Details popup for a top process, and its right-click actions.</summary>
internal sealed class ProcessPopup : Win32Window
{
    private const string ClassName = "Ember.ProcessPopup";
    private IntPtr _edit, _font;
    private static ProcessPopup? _open;

    /// <summary>Shows the details of a process near the point. One popup at a time.</summary>
    public static void Show(int pid, POINT at, Dictionary<int, double>? gpuByPid)
    {
        _open?.Destroy();
        var p = new ProcessPopup();
        p.Create(pid, at, Details(pid, gpuByPid));
        _open = p;
    }

    private void Create(int pid, POINT at, string text)
    {
        RegisterClass(ClassName, IntPtr.Zero, W.GetSysColorBrush(W.COLOR_WINDOW));
        var mon = W.MonitorFromPoint(at, W.MONITOR_DEFAULTTONEAREST);
        float scale = Monitors.Dpi(mon) / 96f;
        int w = (int)(560 * scale), h = (int)(250 * scale);
        var work = Monitors.All().FirstOrDefault(m => m.Handle == mon).Work;
        int x = Math.Clamp(at.X, work.Left, Math.Max(work.Left, work.Right - w));
        int y = Math.Clamp(at.Y, work.Top, Math.Max(work.Top, work.Bottom - h));
        CreateHandle(ClassName, "Ember — process " + pid.ToString(CultureInfo.InvariantCulture),
            W.WS_EX_TOOLWINDOW, W.WS_POPUP | W.WS_CAPTION | W.WS_SYSMENU | W.WS_THICKFRAME,
            x, y, w, h, IntPtr.Zero);
        W.GetClientRect(Handle, out var rc);
        _edit = W.CreateWindowEx(0, "EDIT", text,
            W.WS_CHILD | W.WS_VISIBLE | W.WS_VSCROLL | W.ES_MULTILINE | W.ES_READONLY | W.ES_AUTOVSCROLL,
            0, 0, rc.Width, rc.Height, Handle, IntPtr.Zero, Instance, IntPtr.Zero);
        _font = W.CreateFont(-(int)(13 * scale), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Consolas");
        W.SendMessage(_edit, W.WM_SETFONT, _font, (IntPtr)1);
        W.ShowWindow(Handle, W.SW_SHOW);
        W.SetForegroundWindow(Handle);
    }

    protected override IntPtr OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case W.WM_SIZE:
                if (_edit != IntPtr.Zero) W.MoveWindow(_edit, 0, 0, W.LoWord(lParam) & 0xFFFF, W.HiWord(lParam) & 0xFFFF, true);
                return IntPtr.Zero;
            case W.WM_ACTIVATE:
                if (((long)wParam & 0xFFFF) == 0) W.PostMessage(Handle, W.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); // closes when it loses focus
                break;
            case W.WM_KEYDOWN:
                if ((int)wParam == 0x1B) W.DestroyWindow(Handle); // Esc
                break;
            case W.WM_DESTROY:
                if (_font != IntPtr.Zero) W.DeleteObject(_font);
                if (_open == this) _open = null;
                return IntPtr.Zero;
        }
        return base.OnMessage(msg, wParam, lParam);
    }

    /// <summary>Name, PID, path, command line, CPU, RAM, GPU, disk, start time.</summary>
    private static string Details(int pid, Dictionary<int, double>? gpuByPid)
    {
        var a = SysInfo.Processes()?.FirstOrDefault(p => p.Pid == pid);
        var t0 = Stopwatch.GetTimestamp();
        Thread.Sleep(250);
        var b = SysInfo.Processes()?.FirstOrDefault(p => p.Pid == pid);
        double sec = Stopwatch.GetElapsedTime(t0).TotalSeconds;

        var sb = new StringBuilder();
        void Row(string label, string? value) => sb.Append(label.PadRight(10)).Append(value ?? "—").Append("\r\n");

        if (b is not { } p)
        {
            Row("PID", pid.ToString(CultureInfo.InvariantCulture));
            Row("State", "process has ended");
            return sb.ToString();
        }
        Row("Name", p.Name);
        Row("PID", pid.ToString(CultureInfo.InvariantCulture) + "   (parent " + p.ParentPid.ToString(CultureInfo.InvariantCulture) + ")");
        Row("Path", ProcessInfo.ImagePath(pid));
        Row("Command", ProcessInfo.CommandLine(pid));
        if (a is { } old && old.CreateTime == p.CreateTime && sec > 0)
        {
            double cpu = (p.CpuTime - old.CpuTime) / 1e7 / sec / Environment.ProcessorCount * 100;
            double io = p.IoBytes >= old.IoBytes ? (p.IoBytes - old.IoBytes) / sec : 0;
            Row("CPU", cpu.ToString("0.0", CultureInfo.InvariantCulture) + " %");
            Row("Disk", Format.BytesPerSecond(io) + "   (read + write, includes network I/O)");
        }
        else
        {
            Row("CPU", null);
            Row("Disk", null);
        }
        Row("RAM", Format.Bytes(p.PrivateBytes) + " private");
        Row("GPU", gpuByPid != null ? gpuByPid.GetValueOrDefault(pid).ToString("0.0", CultureInfo.InvariantCulture) + " %" : null);
        Row("Started", DateTime.FromFileTime(p.CreateTime).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    // ───────── Right-click actions ─────────

    private const int CmdEnd = 1, CmdTree = 2, CmdLocation = 3;

    public static void ShowMenu(IntPtr owner, int pid, POINT pt)
    {
        string name = ProcessInfo.Name(pid) ?? ("PID " + pid);
        int cmd = TrayIcon.ShowMenu(owner, pt,
            (CmdEnd, "End process " + name, false),
            (CmdTree, "End process tree", false),
            (0, "", false),
            (CmdLocation, "Open file location", false));
        switch (cmd)
        {
            case CmdEnd:
                if (W.MessageBox(owner, $"End process {name} (PID {pid})?\n\nUnsaved data in this process will be lost.",
                        "Ember", W.MB_OKCANCEL | W.MB_ICONWARNING | W.MB_DEFBUTTON2 | W.MB_SETFOREGROUND) == W.IDOK)
                {
                    if (!ProcessInfo.Kill(pid)) Log.Write($"end process {pid} failed");
                }
                break;
            case CmdTree:
                EndTree(pid);
                break;
            case CmdLocation:
                var path = ProcessInfo.ImagePath(pid);
                if (path != null) W.ShellExecute(IntPtr.Zero, "open", "explorer.exe", $"/select,\"{path}\"", null, W.SW_SHOWNORMAL);
                break;
        }
    }

    /// <summary>Ends the descendants first, then the process.</summary>
    private static void EndTree(int pid)
    {
        var all = ProcessTracker.Snapshot();
        var children = ProcessTree.Descendants(pid, all);
        children.Reverse(); // deepest first
        foreach (var c in children) ProcessInfo.Kill(c);
        if (!ProcessInfo.Kill(pid)) Log.Write($"end process tree {pid} failed");
    }
}
