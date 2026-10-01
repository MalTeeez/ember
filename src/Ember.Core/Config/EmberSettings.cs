using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ember.Core.Config;

/// <summary>Warning and critical value. If Crit &lt; Warn, lower values are worse.</summary>
public sealed class Threshold
{
    public double Warn { get; set; }
    public double Crit { get; set; }
    public Threshold() { }
    public Threshold(double warn, double crit) { Warn = warn; Crit = crit; }

    /// <summary>True when the value crosses the warning threshold.</summary>
    public bool Crossed(double value) => Crit >= Warn ? value >= Warn : value < Warn;
}

/// <summary>On/off for each item. VRAM and network quality are off by default.</summary>
public sealed class ItemSettings
{
    public bool CpuLoad { get; set; } = true;
    public bool CpuCores { get; set; } = true;
    public bool CpuTemp { get; set; } = true;
    public bool CpuThrottle { get; set; } = true;
    public bool CpuTopProcess { get; set; } = true;
    public bool Network { get; set; } = true;
    public bool NetworkQuality { get; set; } = false;
    public bool GpuLoad { get; set; } = true;
    public bool GpuTemp { get; set; } = true;
    public bool GpuVram { get; set; } = false;
    public bool GpuTopProcess { get; set; } = true;
    public bool MemAvailable { get; set; } = true;
    public bool MemCommit { get; set; } = true;
    public bool MemPageFaults { get; set; } = true;
    public bool MemTopProcess { get; set; } = true;
    public bool DiskBusy { get; set; } = true;
    public bool DiskFree { get; set; } = true;
    public bool DiskTopProcess { get; set; } = true;
    public bool Claude { get; set; } = true;
}

public sealed class ThresholdSettings
{
    // CPU
    public double CoreMinSigma { get; set; } = 10;
    public double CoreSigmaFactor { get; set; } = 2;
    public Threshold CpuTemp { get; set; } = new(75, 90);
    public double ThrottlePerf { get; set; } = 90;
    public double ThrottleMinLoad { get; set; } = 50;
    public double CpuTopProcessLoad { get; set; } = 60;
    // GPU
    public double GpuLoad { get; set; } = 10;
    public Threshold GpuTemp { get; set; } = new(70, 85);
    public Threshold Vram { get; set; } = new(80, 95);
    public double GpuTopProcessLoad { get; set; } = 50;
    // Memory
    public Threshold AvailableRam { get; set; } = new(15, 5);
    public Threshold Commit { get; set; } = new(85, 95);
    public Threshold PageFaults { get; set; } = new(1000, 5000);
    // Disk
    public Threshold DiskActive { get; set; } = new(80, 95);
    public Threshold DiskQueue { get; set; } = new(2, 5);
    public Threshold FreeSpacePercent { get; set; } = new(10, 5);
    public double FreeSpaceGb { get; set; } = 20;
    // Network quality
    public Threshold PacketLoss { get; set; } = new(5, 20);
    public Threshold GatewayLatency { get; set; } = new(20, 50);
    public Threshold InternetLatency { get; set; } = new(100, 200);
    // Claude: ratio (time to reset) / (time to limit). 1 = limit exactly at reset.
    public Threshold ClaudeProjection { get; set; } = new(1, 2);
}

public sealed class IntervalSettings
{
    public double SensorSeconds { get; set; } = 1;
    public double ClaudeSeconds { get; set; } = 60;
    public double ShowDelaySeconds { get; set; } = 5;
    public double HideDelaySeconds { get; set; } = 10;
}

public sealed class NetworkSettings
{
    public List<string> ExcludedAdapters { get; set; } = new()
    {
        "Tailscale", "WireGuard", "Wintun", "ZeroTier", "TAP-Windows", "OpenVPN",
        "Hyper-V", "vEthernet", "VirtualBox", "VMware",
    };
    public string InternetHost { get; set; } = "1.1.1.1";
}

public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

public sealed class LayoutSettings
{
    /// <summary>Monitor index. 0 = primary monitor, then the others from left to right.</summary>
    public int Monitor { get; set; } = 0;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Corner Corner { get; set; } = Corner.TopRight;
    /// <summary>Distance from the corner of the work area, in DIP (1/96 inch).</summary>
    public int OffsetX { get; set; } = 24;
    public int OffsetY { get; set; } = 24;
    public double FontSize { get; set; } = 10;
    /// <summary>0..100.</summary>
    public int Opacity { get; set; } = 90;
    public string Font { get; set; } = "Cascadia Mono";
}

public sealed class NotesSettings
{
    public string NewNoteHotkey { get; set; } = "Ctrl+Alt+N";
}

public sealed class EmberSettings
{
    public ItemSettings Items { get; set; } = new();
    public ThresholdSettings Thresholds { get; set; } = new();
    public IntervalSettings Intervals { get; set; } = new();
    public NetworkSettings Network { get; set; } = new();
    public LayoutSettings Layout { get; set; } = new();
    public NotesSettings Notes { get; set; } = new();
    public bool DebugLog { get; set; } = false;

    /// <summary>Clamp values that would break the app.</summary>
    public void Normalize()
    {
        Items ??= new(); Thresholds ??= new(); Intervals ??= new(); Network ??= new(); Layout ??= new(); Notes ??= new();
        Network.ExcludedAdapters ??= new();
        Intervals.SensorSeconds = Math.Clamp(Intervals.SensorSeconds, 0.25, 60);
        Intervals.ClaudeSeconds = Math.Clamp(Intervals.ClaudeSeconds, 15, 3600);
        Intervals.ShowDelaySeconds = Math.Clamp(Intervals.ShowDelaySeconds, 0, 600);
        Intervals.HideDelaySeconds = Math.Clamp(Intervals.HideDelaySeconds, 0, 600);
        Layout.FontSize = Math.Clamp(Layout.FontSize, 6, 32);
        Layout.Opacity = Math.Clamp(Layout.Opacity, 10, 100);
        Layout.Monitor = Math.Max(0, Layout.Monitor);
        if (string.IsNullOrWhiteSpace(Layout.Font)) Layout.Font = "Cascadia Mono";
        Notes.NewNoteHotkey ??= "";
        Network.InternetHost = string.IsNullOrWhiteSpace(Network.InternetHost) ? "1.1.1.1" : Network.InternetHost.Trim();
    }
}

/// <summary>Reads and writes settings.json. A broken file gives default settings.</summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static EmberSettings Parse(string json)
    {
        try
        {
            var s = JsonSerializer.Deserialize<EmberSettings>(json, Options) ?? new EmberSettings();
            s.Normalize();
            return s;
        }
        catch (JsonException)
        {
            return new EmberSettings();
        }
    }

    public static string Serialize(EmberSettings s) => JsonSerializer.Serialize(s, Options);

    public static EmberSettings Load(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : new EmberSettings();
        }
        catch (IOException) { return new EmberSettings(); }
        catch (UnauthorizedAccessException) { return new EmberSettings(); }
    }

    /// <summary>Atomic write: temp file, then replace.</summary>
    public static void Save(string path, EmberSettings s)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, Serialize(s));
        File.Move(tmp, path, overwrite: true);
    }

    public static EmberSettings Clone(EmberSettings s) => Parse(Serialize(s));
}
