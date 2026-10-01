using System.Globalization;

namespace Ember.Core.Config;

public enum FieldKind { Bool, Number, Text, List, Choice }

/// <summary>
/// One row in the settings window. The window is generated from this list, so all
/// parsing and validation is here and is testable.
/// </summary>
public sealed class SettingField
{
    public required string Section { get; init; }
    public required string Label { get; init; }
    public required FieldKind Kind { get; init; }
    public string[]? Choices { get; init; }
    public Func<EmberSettings, string> Get { get; init; } = _ => "";
    /// <summary>Returns false if the text is not valid (the setting does not change).</summary>
    public Func<EmberSettings, string, bool> Set { get; init; } = (_, _) => false;
    /// <summary>True for the start-at-logon row (handled by the app, not stored in settings.json).</summary>
    public bool IsSpecial { get; init; }
}

public static class SettingsSchema
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Num(double v) => v.ToString("0.###", Inv);

    public static bool TryNum(string text, double min, double max, out double v)
    {
        text = text.Trim().Replace(',', '.');
        if (double.TryParse(text, NumberStyles.Float, Inv, out v) && v >= min && v <= max && !double.IsNaN(v))
            return true;
        v = 0;
        return false;
    }

    private static SettingField Bool(string section, string label, Func<EmberSettings, bool> get, Action<EmberSettings, bool> set) => new()
    {
        Section = section, Label = label, Kind = FieldKind.Bool,
        Get = s => get(s) ? "1" : "0",
        Set = (s, t) =>
        {
            if (t is "1" or "true" or "True") { set(s, true); return true; }
            if (t is "0" or "false" or "False") { set(s, false); return true; }
            return false;
        },
    };

    private static SettingField Number(string section, string label, double min, double max,
        Func<EmberSettings, double> get, Action<EmberSettings, double> set) => new()
    {
        Section = section, Label = label, Kind = FieldKind.Number,
        Get = s => Num(get(s)),
        Set = (s, t) =>
        {
            if (!TryNum(t, min, max, out var v)) return false;
            set(s, v);
            return true;
        },
    };

    private static IEnumerable<SettingField> Pair(string section, string label, double min, double max, Func<EmberSettings, Threshold> get)
    {
        yield return Number(section, label + " warning", min, max, s => get(s).Warn, (s, v) => get(s).Warn = v);
        yield return Number(section, label + " critical", min, max, s => get(s).Crit, (s, v) => get(s).Crit = v);
    }

    public static IReadOnlyList<SettingField> Fields { get; } = Build().ToList();

    private static IEnumerable<SettingField> Build()
    {
        const string I = "Items";
        yield return Bool(I, "CPU load", s => s.Items.CpuLoad, (s, v) => s.Items.CpuLoad = v);
        yield return Bool(I, "CPU outlier cores", s => s.Items.CpuCores, (s, v) => s.Items.CpuCores = v);
        yield return Bool(I, "CPU temperature", s => s.Items.CpuTemp, (s, v) => s.Items.CpuTemp = v);
        yield return Bool(I, "CPU throttling", s => s.Items.CpuThrottle, (s, v) => s.Items.CpuThrottle = v);
        yield return Bool(I, "CPU top process", s => s.Items.CpuTopProcess, (s, v) => s.Items.CpuTopProcess = v);
        yield return Bool(I, "Network down/up", s => s.Items.Network, (s, v) => s.Items.Network = v);
        yield return Bool(I, "Network quality (ping)", s => s.Items.NetworkQuality, (s, v) => s.Items.NetworkQuality = v);
        yield return Bool(I, "GPU load", s => s.Items.GpuLoad, (s, v) => s.Items.GpuLoad = v);
        yield return Bool(I, "GPU temperature", s => s.Items.GpuTemp, (s, v) => s.Items.GpuTemp = v);
        yield return Bool(I, "GPU VRAM", s => s.Items.GpuVram, (s, v) => s.Items.GpuVram = v);
        yield return Bool(I, "GPU top process", s => s.Items.GpuTopProcess, (s, v) => s.Items.GpuTopProcess = v);
        yield return Bool(I, "Available RAM", s => s.Items.MemAvailable, (s, v) => s.Items.MemAvailable = v);
        yield return Bool(I, "Commit charge", s => s.Items.MemCommit, (s, v) => s.Items.MemCommit = v);
        yield return Bool(I, "Hard page faults", s => s.Items.MemPageFaults, (s, v) => s.Items.MemPageFaults = v);
        yield return Bool(I, "Top memory process", s => s.Items.MemTopProcess, (s, v) => s.Items.MemTopProcess = v);
        yield return Bool(I, "Disk busy", s => s.Items.DiskBusy, (s, v) => s.Items.DiskBusy = v);
        yield return Bool(I, "Disk free space", s => s.Items.DiskFree, (s, v) => s.Items.DiskFree = v);
        yield return Bool(I, "Top disk process", s => s.Items.DiskTopProcess, (s, v) => s.Items.DiskTopProcess = v);
        yield return Bool(I, "Claude usage", s => s.Items.Claude, (s, v) => s.Items.Claude = v);

        const string T = "Thresholds";
        yield return Number(T, "Core outlier min σ (points)", 0, 100, s => s.Thresholds.CoreMinSigma, (s, v) => s.Thresholds.CoreMinSigma = v);
        yield return Number(T, "Core outlier factor (× σ)", 0, 10, s => s.Thresholds.CoreSigmaFactor, (s, v) => s.Thresholds.CoreSigmaFactor = v);
        foreach (var f in Pair(T, "CPU temperature °C", 0, 150, s => s.Thresholds.CpuTemp)) yield return f;
        yield return Number(T, "Throttling: performance below %", 0, 200, s => s.Thresholds.ThrottlePerf, (s, v) => s.Thresholds.ThrottlePerf = v);
        yield return Number(T, "Throttling: min load %", 0, 100, s => s.Thresholds.ThrottleMinLoad, (s, v) => s.Thresholds.ThrottleMinLoad = v);
        yield return Number(T, "CPU top process: load %", 0, 100, s => s.Thresholds.CpuTopProcessLoad, (s, v) => s.Thresholds.CpuTopProcessLoad = v);
        yield return Number(T, "GPU load %", 0, 100, s => s.Thresholds.GpuLoad, (s, v) => s.Thresholds.GpuLoad = v);
        foreach (var f in Pair(T, "GPU temperature °C", 0, 150, s => s.Thresholds.GpuTemp)) yield return f;
        foreach (var f in Pair(T, "VRAM used %", 0, 100, s => s.Thresholds.Vram)) yield return f;
        yield return Number(T, "GPU top process: load %", 0, 100, s => s.Thresholds.GpuTopProcessLoad, (s, v) => s.Thresholds.GpuTopProcessLoad = v);
        foreach (var f in Pair(T, "Available RAM %", 0, 100, s => s.Thresholds.AvailableRam)) yield return f;
        foreach (var f in Pair(T, "Commit charge %", 0, 100, s => s.Thresholds.Commit)) yield return f;
        foreach (var f in Pair(T, "Hard page faults /s", 0, 1e7, s => s.Thresholds.PageFaults)) yield return f;
        foreach (var f in Pair(T, "Disk active %", 0, 100, s => s.Thresholds.DiskActive)) yield return f;
        foreach (var f in Pair(T, "Disk queue length", 0, 1000, s => s.Thresholds.DiskQueue)) yield return f;
        foreach (var f in Pair(T, "Free space %", 0, 100, s => s.Thresholds.FreeSpacePercent)) yield return f;
        yield return Number(T, "Free space warning GB", 0, 1e6, s => s.Thresholds.FreeSpaceGb, (s, v) => s.Thresholds.FreeSpaceGb = v);
        foreach (var f in Pair(T, "Packet loss %", 0, 100, s => s.Thresholds.PacketLoss)) yield return f;
        foreach (var f in Pair(T, "Gateway latency ms", 0, 10000, s => s.Thresholds.GatewayLatency)) yield return f;
        foreach (var f in Pair(T, "Internet latency ms", 0, 10000, s => s.Thresholds.InternetLatency)) yield return f;
        foreach (var f in Pair(T, "Claude: reset ÷ limit time", 0, 100, s => s.Thresholds.ClaudeProjection)) yield return f;

        const string V = "Intervals";
        yield return Number(V, "Sensors (s)", 0.25, 60, s => s.Intervals.SensorSeconds, (s, v) => s.Intervals.SensorSeconds = v);
        yield return Number(V, "Claude usage (s)", 15, 3600, s => s.Intervals.ClaudeSeconds, (s, v) => s.Intervals.ClaudeSeconds = v);
        yield return Number(V, "Show delay (s)", 0, 600, s => s.Intervals.ShowDelaySeconds, (s, v) => s.Intervals.ShowDelaySeconds = v);
        yield return Number(V, "Hide delay (s)", 0, 600, s => s.Intervals.HideDelaySeconds, (s, v) => s.Intervals.HideDelaySeconds = v);

        const string N = "Network";
        yield return new SettingField
        {
            Section = N, Label = "Excluded adapters (one per line)", Kind = FieldKind.List,
            Get = s => string.Join("\r\n", s.Network.ExcludedAdapters),
            Set = (s, t) =>
            {
                s.Network.ExcludedAdapters = t.Split('\n')
                    .Select(x => x.Trim('\r', ' ', '\t'))
                    .Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                return true;
            },
        };
        yield return new SettingField
        {
            Section = N, Label = "Internet ping host", Kind = FieldKind.Text,
            Get = s => s.Network.InternetHost,
            Set = (s, t) =>
            {
                t = t.Trim();
                if (t.Length == 0 || t.Contains(' ')) return false;
                s.Network.InternetHost = t;
                return true;
            },
        };

        const string L = "Layout";
        yield return Number(L, "Monitor (0 = primary)", 0, 16, s => s.Layout.Monitor, (s, v) => s.Layout.Monitor = (int)v);
        yield return new SettingField
        {
            Section = L, Label = "Corner", Kind = FieldKind.Choice,
            Choices = Enum.GetNames<Corner>(),
            Get = s => s.Layout.Corner.ToString(),
            Set = (s, t) =>
            {
                if (!Enum.TryParse<Corner>(t.Trim(), true, out var c)) return false;
                s.Layout.Corner = c;
                return true;
            },
        };
        yield return Number(L, "Offset X (px at 100%)", -10000, 10000, s => s.Layout.OffsetX, (s, v) => s.Layout.OffsetX = (int)v);
        yield return Number(L, "Offset Y (px at 100%)", -10000, 10000, s => s.Layout.OffsetY, (s, v) => s.Layout.OffsetY = (int)v);
        yield return Number(L, "Font size (pt)", 6, 32, s => s.Layout.FontSize, (s, v) => s.Layout.FontSize = v);
        yield return Number(L, "Opacity %", 10, 100, s => s.Layout.Opacity, (s, v) => s.Layout.Opacity = (int)v);

        const string O = "Other";
        yield return new SettingField
        {
            Section = O, Label = "New note hotkey", Kind = FieldKind.Text,
            Get = s => s.Notes.NewNoteHotkey,
            Set = (s, t) =>
            {
                t = t.Trim();
                if (t.Length > 0 && !Util.Hotkey.TryParse(t, out _, out _)) return false;
                s.Notes.NewNoteHotkey = t;
                return true;
            },
        };
        yield return new SettingField
        {
            Section = O, Label = "Start at logon (asks for admin)", Kind = FieldKind.Bool, IsSpecial = true,
        };
        yield return Bool(O, "Debug log", s => s.DebugLog, (s, v) => s.DebugLog = v);
    }
}
