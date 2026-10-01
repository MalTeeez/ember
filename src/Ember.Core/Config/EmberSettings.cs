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

/// <summary>
/// Optional fields inside lines. Lines themselves are switched on/off (and ordered) in
/// <see cref="EmberSettings.Entries"/>.
/// </summary>
public sealed class FieldSettings
{
    public bool CpuTemp { get; set; } = true;
    public bool CpuClock { get; set; } = true;
    /// <summary>CPU temperature on the cpu line always (else only when it crosses its warning value; the graph is wider then).</summary>
    public bool CpuTempAlways { get; set; } = false;
    /// <summary>Temperature and clock on hot-core lines.</summary>
    public bool CoreDetails { get; set; } = true;
    public bool GpuTemp { get; set; } = true;
    /// <summary>Claude: third line with the projection ("will last" / "empty … before reset").</summary>
    public bool ClaudeProjection { get; set; } = true;
    /// <summary>Network in bytes per second instead of bits.</summary>
    public bool NetBytes { get; set; } = false;
    /// <summary>Device batteries: always shown (false = only when below the warning value).</summary>
    public bool BatteryAlways { get; set; } = true;
}

public sealed class ThresholdSettings
{
    // CPU
    /// <summary>A core with at least this load (smoothed) shows as a hot core, also when it is no outlier.</summary>
    public double HotCoreLoad { get; set; } = 85;
    /// <summary>A core this many °C above the median core temperature is hot, also at low load.</summary>
    public double HotCoreTempDelta { get; set; } = 10;
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
    /// <summary>Soft page faults per second (shown only without hard paging).</summary>
    public Threshold SoftPageFaults { get; set; } = new(25000, 100000);
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
    // Device batteries (lower is worse)
    public Threshold Battery { get; set; } = new(30, 15);
}

public sealed class IntervalSettings
{
    public double SensorSeconds { get; set; } = 1;
    public double ClaudeSeconds { get; set; } = 120;
    public double CodexSeconds { get; set; } = 60;
    public double CopilotSeconds { get; set; } = 300;
    public double BatterySeconds { get; set; } = 60;
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
    /// <summary>Show the pinged host (gateway address, internet host) on the ping lines.</summary>
    public bool ShowPingHost { get; set; } = true;
    /// <summary>Show the ping lines always (coloured by the thresholds), not only when slow.</summary>
    public bool PingAlways { get; set; } = false;
    /// <summary>Also a ping line for the local gateway (router).</summary>
    public bool ShowGatewayPing { get; set; } = false;
    /// <summary>Hops to the internet host under its ping line (traceroute every 30 s).</summary>
    public bool ShowTraceroute { get; set; } = false;
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
    /// <summary>0..100: opacity of the text and graphs (the background has its own opacity in the theme).</summary>
    public int Opacity { get; set; } = 100;
    public string Font { get; set; } = "Cascadia Mono";
    /// <summary>Space of an item that got smaller is kept this long (no jumping lines).</summary>
    public double HoldSeconds { get; set; } = 60;
    /// <summary>A top process stays until another one is higher for this long.</summary>
    public double ProcessStickSeconds { get; set; } = 15;
    /// <summary>Animate size changes.</summary>
    /// <summary>Where kept space (empty lines of shrunk items) goes: "bottom" of the widget, or "inline" (where the item was).</summary>
    public string KeptSpace { get; set; } = "section";
    public bool Animate { get; set; } = true;
    public int AnimationMs { get; set; } = 160;
}

public sealed class NotesSettings
{
    public string NewNoteHotkey { get; set; } = "Ctrl+Alt+N";
    /// <summary>Opacity of the notes in % (30..100). Independent of the widget's opacity settings.</summary>
    public int Opacity { get; set; } = 100;
}

public sealed class EmberSettings
{
    public FieldSettings Fields { get; set; } = new();
    public List<LayoutEntry> Entries { get; set; } = LayoutPreset.Default().Entries;
    /// <summary>Name of the last loaded layout preset (shown in the preset list).</summary>
    public string LayoutName { get; set; } = "default";
    /// <summary>Version of the layout format; older layouts are migrated in <see cref="Normalize"/>.</summary>
    public int LayoutVersion { get; set; }
    public Ember.Core.Render.Theme Theme { get; set; } = new();
    /// <summary>Debug: show every enabled item, ignore thresholds and delays, fill missing data with sample values.</summary>
    public bool PreviewAll { get; set; } = false;
    public ThresholdSettings Thresholds { get; set; } = new();
    public IntervalSettings Intervals { get; set; } = new();
    public NetworkSettings Network { get; set; } = new();
    public LayoutSettings Layout { get; set; } = new();
    public NotesSettings Notes { get; set; } = new();
    public bool DebugLog { get; set; } = false;
    /// <summary>Extended debug log: show/hide decisions, kept space, top process changes, widget size.</summary>
    public bool DebugDecisions { get; set; } = false;

    /// <summary>True if the layout has the item and it is on.</summary>
    public bool IsOn(string id) => Entries.Any(e => e.Kind == "item" && e.Id == id && e.Enabled);

    /// <summary>Clamp values that would break the app.</summary>
    public void Normalize()
    {
        Fields ??= new(); Theme ??= new(); Theme.Background ??= new(); Theme.GraphStops ??= new();
        Layout ??= new();
        Entries = LayoutPreset.Normalize(Entries, addMissingAsDefault: true);
        if (LayoutVersion < LayoutPreset.CurrentVersion)
        {
            // Version 3: "bottom" was the default before "section" existed.
            if (LayoutVersion < 3 && Layout.KeptSpace == "bottom") Layout.KeptSpace = "section";
            // Version 3: the temperature shows when hot; the graph uses its place otherwise.
            if (LayoutVersion < 3) Fields.CpuTempAlways = false;
            LayoutPreset.Migrate(Entries, LayoutVersion);
            LayoutVersion = LayoutPreset.CurrentVersion;
        }
        LayoutName ??= "";
        Thresholds ??= new(); Intervals ??= new(); Network ??= new(); Layout ??= new(); Notes ??= new();
        Network.ExcludedAdapters ??= new();
        Intervals.SensorSeconds = Math.Clamp(Intervals.SensorSeconds, 0.25, 60);
        Intervals.ClaudeSeconds = Math.Clamp(Intervals.ClaudeSeconds, 120, 3600);
        Intervals.CodexSeconds = Math.Clamp(Intervals.CodexSeconds, 60, 3600);
        Intervals.CopilotSeconds = Math.Clamp(Intervals.CopilotSeconds, 60, 3600);
        Intervals.BatterySeconds = Math.Clamp(Intervals.BatterySeconds, 10, 3600);
        Intervals.ShowDelaySeconds = Math.Clamp(Intervals.ShowDelaySeconds, 0, 600);
        Intervals.HideDelaySeconds = Math.Clamp(Intervals.HideDelaySeconds, 0, 600);
        Layout.FontSize = Math.Clamp(Layout.FontSize, 6, 32);
        Layout.Opacity = Math.Clamp(Layout.Opacity, 10, 100);
        Layout.HoldSeconds = Math.Clamp(Layout.HoldSeconds, 0, 3600);
        Layout.ProcessStickSeconds = Math.Clamp(Layout.ProcessStickSeconds, 0, 600);
        Layout.AnimationMs = Math.Clamp(Layout.AnimationMs, 0, 1000);
        Theme.Background.Opacity = Math.Clamp(Theme.Background.Opacity, 0, 100);
        Theme.Background.BorderOpacity = Math.Clamp(Theme.Background.BorderOpacity, 0, 100);
        Theme.Background.CornerRadius = Math.Clamp(Theme.Background.CornerRadius, 0, 40);
        Theme.Background.Padding = Math.Clamp(Theme.Background.Padding, 0, 40);
        Layout.Monitor = Math.Max(0, Layout.Monitor);
        if (string.IsNullOrWhiteSpace(Layout.Font)) Layout.Font = "Cascadia Mono";
        Notes.NewNoteHotkey ??= "";
        Layout.KeptSpace = Layout.KeptSpace is "inline" or "bottom" ? Layout.KeptSpace : "section";
        Notes.Opacity = Math.Clamp(Notes.Opacity, 30, 100);
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
