using System.Runtime.InteropServices;
using Ember.App;
using Ember.Core.Devices;
using Microsoft.Win32.SafeHandles;

namespace Ember.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct SP_DEVINFO_DATA { public uint cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }

[StructLayout(LayoutKind.Sequential)]
internal struct SP_DEVICE_INTERFACE_DATA { public uint cbSize; public Guid InterfaceClassGuid; public uint Flags; public IntPtr Reserved; }

[StructLayout(LayoutKind.Sequential)]
internal struct DEVPROPKEY { public Guid fmtid; public uint pid; public DEVPROPKEY(Guid g, uint p) { fmtid = g; pid = p; } }

[StructLayout(LayoutKind.Sequential)]
internal struct HIDD_ATTRIBUTES { public uint Size; public ushort VendorID, ProductID, VersionNumber; }

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct HIDP_CAPS
{
    public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
    public fixed ushort Reserved[17];
    public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
        NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices, NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
}

/// <summary>
/// Device batteries:
/// 1. Bluetooth (classic and LE): the level Windows itself knows (the same as in Settings → Bluetooth),
///    device property DEVPKEY_Bluetooth_Battery {104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2 (a byte, 0..100).
/// 2. Razer 2.4 GHz receivers (HyperSpeed): Razer feature report, power class 0x07, battery 0x80, charging 0x84.
/// </summary>
internal static class DeviceBatteries
{
    private const uint DIGCF_PRESENT = 0x2, DIGCF_ALLCLASSES = 0x4, DIGCF_DEVICEINTERFACE = 0x10;
    private const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000, FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
    private static readonly IntPtr InvalidHandle = new(-1);

    private static readonly DEVPROPKEY BluetoothBattery = new(new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), 2);
    private static readonly DEVPROPKEY FriendlyName = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
    private static readonly DEVPROPKEY DeviceDesc = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 2);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string? enumerator, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SP_DEVINFO_DATA data);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid iface, uint index, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, uint size, out uint required, IntPtr devInfo);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceProperty(IntPtr set, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key, out uint type, byte[]? buffer, uint size, out uint required, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES attr);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetProductString(SafeFileHandle h, char[] buffer, uint length);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, out HIDP_CAPS caps);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_SetFeature(SafeFileHandle h, byte[] buffer, uint length);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buffer, uint length);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    // ───────── Bluetooth ─────────

    /// <summary>Bluetooth devices with a battery level that Windows knows.</summary>
    public static List<DeviceBattery> Bluetooth(List<string>? trace = null)
    {
        var result = new List<DeviceBattery>();
        foreach (var enumerator in new[] { "BTHENUM", "BTHLE", "BTHLEDEVICE" })
        {
            IntPtr set = SetupDiGetClassDevs(IntPtr.Zero, enumerator, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
            if (set == InvalidHandle) continue;
            try
            {
                var data = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
                for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
                {
                    var key = BluetoothBattery;
                    var buf = new byte[4];
                    bool has = SetupDiGetDeviceProperty(set, ref data, ref key, out _, buf, (uint)buf.Length, out uint req, 0) && req >= 1;
                    string name = StringProperty(set, ref data, FriendlyName) ?? StringProperty(set, ref data, DeviceDesc) ?? "Bluetooth device";
                    trace?.Add($"bt {enumerator}: {name}: " + (has ? $"battery {buf[0]}" : "no battery value"));
                    if (!has) continue;
                    int level = buf[0];
                    if (level > 100) continue;
                    result.Add(new DeviceBattery(name, level, null, "bluetooth"));
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
        }
        return result;
    }

    private static string? StringProperty(IntPtr set, ref SP_DEVINFO_DATA data, DEVPROPKEY key)
    {
        SetupDiGetDeviceProperty(set, ref data, ref key, out _, null, 0, out uint size, 0);
        if (size < 2 || size > 4096) return null;
        var buf = new byte[size];
        if (!SetupDiGetDeviceProperty(set, ref data, ref key, out _, buf, size, out _, 0)) return null;
        return System.Text.Encoding.Unicode.GetString(buf).TrimEnd('\0');
    }

    // ───────── Razer ─────────

    /// <summary>Remembered transaction id for each device path (the one that answered).</summary>
    private static readonly Dictionary<string, byte> TransactionIds = new();

    /// <summary>Razer wireless receivers that answer the battery command.</summary>
    public static List<DeviceBattery> Razer(List<string>? trace = null)
    {
        var result = new List<DeviceBattery>();
        HidD_GetHidGuid(out var hidGuid);
        IntPtr set = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == InvalidHandle) return result;
        var seen = new HashSet<ushort>();
        try
        {
            var iface = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref iface); i++)
            {
                string? path = InterfacePath(set, ref iface);
                if (path == null || !path.Contains("vid_1532", StringComparison.OrdinalIgnoreCase)) continue;
                var battery = QueryRazer(path, seen, trace);
                if (battery != null) result.Add(battery);
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }

    private static string? InterfacePath(IntPtr set, ref SP_DEVICE_INTERFACE_DATA iface)
    {
        SetupDiGetDeviceInterfaceDetail(set, ref iface, IntPtr.Zero, 0, out uint size, IntPtr.Zero);
        if (size == 0) return null;
        IntPtr buf = Marshal.AllocHGlobal((int)size);
        try
        {
            Marshal.WriteInt32(buf, 8); // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W on x64
            if (!SetupDiGetDeviceInterfaceDetail(set, ref iface, buf, size, out _, IntPtr.Zero)) return null;
            return Marshal.PtrToStringUni(buf + 4);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static DeviceBattery? QueryRazer(string path, HashSet<ushort> seen, List<string>? trace)
    {
        string shortPath = path.Length > 70 ? path[..70] + "…" : path;
        using var h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h.IsInvalid)
        {
            // Mouse/keyboard collections are owned by Windows; the vendor collection opens.
            trace?.Add($"razer {shortPath}: cannot open (error {Marshal.GetLastWin32Error()}; normal for mouse/keyboard collections)");
            return null;
        }
        var attr = new HIDD_ATTRIBUTES { Size = (uint)Marshal.SizeOf<HIDD_ATTRIBUTES>() };
        if (!HidD_GetAttributes(h, ref attr) || attr.VendorID != RazerProtocol.VendorId) { trace?.Add($"razer {shortPath}: no attributes"); return null; }
        if (seen.Contains(attr.ProductID)) { trace?.Add($"razer {attr.ProductID:X4}: already answered on another interface"); return null; }
        if (!HidD_GetPreparsedData(h, out var pre)) { trace?.Add($"razer {attr.ProductID:X4}: no HID caps"); return null; }
        HIDP_CAPS caps;
        try { HidP_GetCaps(pre, out caps); }
        finally { HidD_FreePreparsedData(pre); }
        var nameBuf = new char[127];
        string? product = HidD_GetProductString(h, nameBuf, (uint)(nameBuf.Length * 2)) ? new string(nameBuf).TrimEnd('\0') : null;
        string info = $"razer {attr.ProductID:X4} '{product}' usage {caps.UsagePage:X4}/{caps.Usage:X4} feature {caps.FeatureReportByteLength} B";
        // The Razer protocol uses a 91-byte feature report (report id + 90 bytes). Wired mice answer too.
        if (caps.FeatureReportByteLength != RazerProtocol.ReportLength + 1) { trace?.Add(info + ": not the Razer control interface"); return null; }
        string name = RazerProtocol.KnownReceivers.TryGetValue(attr.ProductID, out var known) ? known : product is { Length: > 0 } ? product : $"Razer {attr.ProductID:X4}";

        var ids = TransactionIds.TryGetValue(path, out byte remembered) ? new[] { remembered } : RazerProtocol.TransactionIds;
        foreach (byte tid in ids)
        {
            var battery = Send(h, tid, RazerProtocol.CmdBattery);
            if (battery == null) { trace?.Add(info + $": tid {tid:X2}: no answer"); continue; }
            double? percent = RazerProtocol.BatteryPercent(battery);
            trace?.Add(info + $": tid {tid:X2}: status {battery[0]:X2}, battery " + (percent is { } pc ? $"{pc:0}%" : "none"));
            if (percent == null) continue;
            TransactionIds[path] = tid;
            bool? charging = Send(h, tid, RazerProtocol.CmdCharging) is { } c ? RazerProtocol.Charging(c) : null;
            seen.Add(attr.ProductID);
            return new DeviceBattery(name, percent.Value, charging, "razer");
        }
        TransactionIds.Remove(path);
        return null;
    }

    /// <summary>Sends one command and reads the answer (the device needs a short time to answer).</summary>
    private static byte[]? Send(SafeFileHandle h, byte tid, byte command)
    {
        var request = RazerProtocol.Request(tid, RazerProtocol.ClassPower, command, 0x02);
        var buf = new byte[RazerProtocol.ReportLength + 1];
        Buffer.BlockCopy(request, 0, buf, 1, request.Length);
        if (!HidD_SetFeature(h, buf, (uint)buf.Length)) return null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Thread.Sleep(60);
            var resp = new byte[RazerProtocol.ReportLength + 1];
            if (!HidD_GetFeature(h, resp, (uint)resp.Length)) return null;
            var report = resp.AsSpan(1).ToArray();
            if (report[0] == RazerProtocol.StatusBusy) continue;
            return report;
        }
        return null;
    }
}
