using System.Globalization;
using Ember.Core.Render;

namespace Ember.Core.Config;

public enum FieldKind { Bool, Number, Slider, Text, List, Choice, Color, Action, LayoutEditor }

/// <summary>
/// One row in the settings window. The window is generated from this list, so all
/// parsing and validation is here and is testable. Rows are grouped in pages, and in sections inside a page.
/// </summary>
public sealed class SettingField
{
    public required string Page { get; init; }
    public string Section { get; init; } = "";
    public required string Label { get; init; }
    public required FieldKind Kind { get; init; }
    public string[]? Choices { get; init; }
    public double Min { get; init; }
    public double Max { get; init; } = 100;
    /// <summary>Slider step.</summary>
    public double Step { get; init; } = 1;
    /// <summary>Action id for <see cref="FieldKind.Action"/> rows (handled by the app).</summary>
    public string? Action { get; init; }
    public string? Tooltip { get; init; }
    public Func<EmberSettings, string> Get { get; init; } = _ => "";
    /// <summary>Returns false if the text is not valid (the setting does not change).</summary>
    public Func<EmberSettings, string, bool> Set { get; init; } = (_, _) => false;
    /// <summary>True for the start-at-logon row (handled by the app, not stored in settings.json).</summary>
    public bool IsSpecial { get; init; }
    /// <summary>A change can change other rows (theme preset, layout preset): the window reloads all values.</summary>
    public bool ReloadsOthers { get; init; }
}

public static class SettingsSchema
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static readonly string[] Pages = { "General", "Layout", "Appearance", "Thresholds", "Intervals", "Network", "Notes" };

    public static string Num(double v) => v.ToString("0.###", Inv);

    public static bool TryNum(string text, double min, double max, out double v)
    {
        text = text.Trim().Replace(',', '.');
        if (double.TryParse(text, NumberStyles.Float, Inv, out v) && v >= min && v <= max && !double.IsNaN(v))
            return true;
        v = 0;
        return false;
    }

    private static SettingField Bool(string page, string section, string label, Func<EmberSettings, bool> get, Action<EmberSettings, bool> set, string? tip = null) => new()
    {
        Page = page, Section = section, Label = label, Kind = FieldKind.Bool, Tooltip = tip,
        Get = s => get(s) ? "1" : "0",
        Set = (s, t) =>
        {
            if (t is "1" or "true" or "True") { set(s, true); return true; }
            if (t is "0" or "false" or "False") { set(s, false); return true; }
            return false;
        },
    };

    private static SettingField Number(string page, string section, string label, double min, double max,
        Func<EmberSettings, double> get, Action<EmberSettings, double> set, FieldKind kind = FieldKind.Number) => new()
    {
        Page = page, Section = section, Label = label, Kind = kind, Min = min, Max = max,
        Get = s => Num(get(s)),
        Set = (s, t) =>
        {
            if (!TryNum(t, min, max, out var v)) return false;
            set(s, v);
            return true;
        },
    };

    private static SettingField Slider(string page, string section, string label, double min, double max,
        Func<EmberSettings, double> get, Action<EmberSettings, double> set, double step = 1)
    {
        var f = Number(page, section, label, min, max, get, set, FieldKind.Slider);
        return new SettingField
        {
            Page = f.Page, Section = f.Section, Label = f.Label, Kind = f.Kind, Min = min, Max = max, Step = step,
            Get = f.Get, Set = f.Set,
        };
    }

    private static SettingField Choice(string page, string section, string label, string[] choices,
        Func<EmberSettings, string> get, Action<EmberSettings, string> set, bool reload = false) => new()
    {
        Page = page, Section = section, Label = label, Kind = FieldKind.Choice, Choices = choices, ReloadsOthers = reload,
        Get = get,
        Set = (s, t) =>
        {
            if (!choices.Contains(t)) return false;
            set(s, t);
            return true;
        },
    };

    private static SettingField Color(string page, string section, string label, Func<EmberSettings, string> get, Action<EmberSettings, string> set) => new()
    {
        Page = page, Section = section, Label = label, Kind = FieldKind.Color,
        Get = s => Rgb.Parse(get(s), Palette.Bright).ToHex(),
        Set = (s, t) =>
        {
            if (!Rgb.TryParse(t, out var c)) return false;
            set(s, c.ToHex());
            return true;
        },
    };

    private static SettingField Action(string page, string section, string label, string action, string? tip = null)
        => new() { Page = page, Section = section, Label = label, Kind = FieldKind.Action, Action = action, Tooltip = tip };

    private static IEnumerable<SettingField> Pair(string page, string label, double min, double max, Func<EmberSettings, Threshold> get)
    {
        yield return Number(page, "", label + " warning", min, max, s => get(s).Warn, (s, v) => get(s).Warn = v);
        yield return Number(page, "", label + " critical", min, max, s => get(s).Crit, (s, v) => get(s).Crit = v);
    }

    public static IReadOnlyList<SettingField> Fields { get; } = Build().ToList();

    public static IEnumerable<SettingField> OnPage(string page) => Fields.Where(f => f.Page == page);

    private static IEnumerable<SettingField> Build()
    {
        // ── General ──
        const string G = "General";
        yield return new SettingField { Page = G, Section = "Start", Label = "Start at logon (asks for admin)", Kind = FieldKind.Bool, IsSpecial = true };
        yield return Slider(G, "Size", "Font size (pt)", 6, 24, s => s.Layout.FontSize, (s, v) => s.Layout.FontSize = Math.Round(v * 2) / 2, 0.5);
        yield return Slider(G, "Size", "Text opacity %", 10, 100, s => s.Layout.Opacity, (s, v) => s.Layout.Opacity = (int)v);
        yield return new SettingField
        {
            Page = G, Section = "Size", Label = "Font", Kind = FieldKind.Text,
            Get = s => s.Layout.Font,
            Set = (s, t) => { t = t.Trim(); if (t.Length == 0) return false; s.Layout.Font = t; return true; },
        };
        yield return Number(G, "Position", "Monitor (0 = primary)", 0, 16, s => s.Layout.Monitor, (s, v) => s.Layout.Monitor = (int)v);
        yield return Choice(G, "Position", "Corner", Enum.GetNames<Corner>(), s => s.Layout.Corner.ToString(),
            (s, t) => s.Layout.Corner = Enum.Parse<Corner>(t));
        yield return Number(G, "Position", "Offset X (px at 100%)", -10000, 10000, s => s.Layout.OffsetX, (s, v) => s.Layout.OffsetX = (int)v);
        yield return Number(G, "Position", "Offset Y (px at 100%)", -10000, 10000, s => s.Layout.OffsetY, (s, v) => s.Layout.OffsetY = (int)v);
        yield return Bool(G, "Motion", "Animate size changes", s => s.Layout.Animate, (s, v) => s.Layout.Animate = v);
        yield return Slider(G, "Motion", "Animation length (ms)", 0, 600, s => s.Layout.AnimationMs, (s, v) => s.Layout.AnimationMs = (int)v);
        yield return Choice(G, "Motion", "Kept space goes to", new[] { "bottom", "inline" }, s => s.Layout.KeptSpace, (s, t) => s.Layout.KeptSpace = t);
        yield return Slider(G, "Motion", "Keep space of shrunk items (s)", 0, 600, s => s.Layout.HoldSeconds, (s, v) => s.Layout.HoldSeconds = v);
        yield return Slider(G, "Motion", "Keep top process at least (s)", 0, 120, s => s.Layout.ProcessStickSeconds, (s, v) => s.Layout.ProcessStickSeconds = v);
        yield return Bool(G, "Debug", "Show everything (ignore thresholds, sample data)", s => s.PreviewAll, (s, v) => s.PreviewAll = v,
            "For theming: every enabled item shows, missing data gets sample values.");
        yield return Bool(G, "Debug", "Debug log", s => s.DebugLog, (s, v) => s.DebugLog = v);
        yield return Bool(G, "Debug", "Extended: log show/hide and resize decisions", s => s.DebugDecisions, (s, v) => s.DebugDecisions = v,
            "Needs the debug log. Logs when lines show or hide (and why), kept space, top process changes and the widget height.");
        yield return Action(G, "Debug", "Open log", "app.openlog");
        yield return Action(G, "Debug", "Open settings file", "app.opensettings");
        yield return Action(G, "Debug", "Open data folder", "app.openfolder");

        // ── Layout ──
        const string L = "Layout";
        yield return Choice(L, "Order and items", "Load layout preset", LayoutPreset.BuiltIn.Select(p => p.Name).Prepend("").ToArray(),
            s => LayoutPreset.BuiltIn.Any(p => p.Name == s.LayoutName) ? s.LayoutName : "",
            (s, t) => { if (LayoutPreset.Find(t) is { } p) { s.Entries = p.Entries; s.LayoutName = p.Name; } }, reload: true);
        yield return new SettingField { Page = L, Section = "Order and items", Label = "Lines", Kind = FieldKind.LayoutEditor };
        yield return Action(L, "Order and items", "Import layout…", "layout.import");
        yield return Action(L, "Order and items", "Export layout…", "layout.export");
        yield return Bool(L, "Fields", "CPU temperature", s => s.Fields.CpuTemp, (s, v) => s.Fields.CpuTemp = v);
        yield return Bool(L, "Fields", "CPU clock", s => s.Fields.CpuClock, (s, v) => s.Fields.CpuClock = v);
        yield return Bool(L, "Fields", "Clock and temperature on hot cores", s => s.Fields.CoreDetails, (s, v) => s.Fields.CoreDetails = v);
        yield return Bool(L, "Fields", "GPU temperature", s => s.Fields.GpuTemp, (s, v) => s.Fields.GpuTemp = v);
        yield return Bool(L, "Fields", "Claude projection line", s => s.Fields.ClaudeProjection, (s, v) => s.Fields.ClaudeProjection = v);
        yield return Bool(L, "Fields", "Network in bytes (not bits)", s => s.Fields.NetBytes, (s, v) => s.Fields.NetBytes = v);
        yield return Bool(L, "Fields", "Device batteries always (off: only when low)", s => s.Fields.BatteryAlways, (s, v) => s.Fields.BatteryAlways = v);

        // ── Appearance ──
        const string A = "Appearance";
        yield return Choice(A, "Theme", "Theme preset", ThemeStore.BuiltIn.Select(t => t.Name).Prepend("").ToArray(),
            s => ThemeStore.BuiltIn.Any(t => t.Name == s.Theme.Name) ? s.Theme.Name : "",
            (s, t) => { if (ThemeStore.Find(t) is { } th) s.Theme = th; }, reload: true);
        yield return Action(A, "Theme", "Import theme…", "theme.import");
        yield return Action(A, "Theme", "Export theme…", "theme.export");
        yield return Choice(A, "Background", "Background", ThemeStore.BackgroundKinds, s => s.Theme.Background.Kind, (s, t) => s.Theme.Background.Kind = t);
        yield return Color(A, "Background", "Background colour", s => s.Theme.Background.Color, (s, v) => s.Theme.Background.Color = v);
        yield return Color(A, "Background", "Second colour (gradient, glass)", s => s.Theme.Background.Color2, (s, v) => s.Theme.Background.Color2 = v);
        yield return Slider(A, "Background", "Background opacity %", 0, 100, s => s.Theme.Background.Opacity, (s, v) => s.Theme.Background.Opacity = (int)v);
        yield return Slider(A, "Background", "Gradient angle", 0, 360, s => s.Theme.Background.GradientAngle, (s, v) => s.Theme.Background.GradientAngle = (int)v);
        yield return Slider(A, "Background", "Corner radius", 0, 40, s => s.Theme.Background.CornerRadius, (s, v) => s.Theme.Background.CornerRadius = (int)v);
        yield return Slider(A, "Background", "Padding", 0, 40, s => s.Theme.Background.Padding, (s, v) => s.Theme.Background.Padding = (int)v);
        yield return Color(A, "Background", "Border colour", s => s.Theme.Background.Border, (s, v) => s.Theme.Background.Border = v);
        yield return Slider(A, "Background", "Border opacity %", 0, 100, s => s.Theme.Background.BorderOpacity, (s, v) => s.Theme.Background.BorderOpacity = (int)v);
        yield return Bool(A, "Background", "Text shadow", s => s.Theme.Background.TextShadow, (s, v) => s.Theme.Background.TextShadow = v);

        yield return Choice(A, "Style", "Section headers", ThemeStore.HeaderStyles, s => s.Theme.HeaderStyle, (s, t) => s.Theme.HeaderStyle = t);
        yield return Choice(A, "Style", "Header alignment", ThemeStore.HeaderAligns, s => s.Theme.HeaderAlign, (s, t) => s.Theme.HeaderAlign = t);
        yield return Slider(A, "Style", "Header indent (characters)", 0, 12, s => s.Theme.HeaderIndent, (s, v) => s.Theme.HeaderIndent = (int)v);
        yield return Bool(A, "Row backgrounds", "Stripes on every second line", s => s.Theme.RowStripes, (s, v) => s.Theme.RowStripes = v);
        yield return Bool(A, "Row backgrounds", "Band behind headers", s => s.Theme.HeaderBand, (s, v) => s.Theme.HeaderBand = v);
        yield return Bool(A, "Row backgrounds", "Band behind process lines", s => s.Theme.ProcessBand, (s, v) => s.Theme.ProcessBand = v);
        yield return Bool(A, "Row backgrounds", "Track behind graphs", s => s.Theme.GraphTrack, (s, v) => s.Theme.GraphTrack = v);
        yield return Bool(A, "Row backgrounds", "Track behind bars", s => s.Theme.BarTrack, (s, v) => s.Theme.BarTrack = v);
        yield return Bool(A, "Row backgrounds", "Chips behind warning values", s => s.Theme.ValueChips, (s, v) => s.Theme.ValueChips = v);
        yield return Slider(A, "Row backgrounds", "Strength %", 0, 300, s => s.Theme.BandOpacity, (s, v) => s.Theme.BandOpacity = (int)v);
        yield return Bool(A, "Style", "Icons in headers", s => s.Theme.HeaderIcons, (s, v) => s.Theme.HeaderIcons = v);
        yield return Bool(A, "Style", "Upper-case headers", s => s.Theme.HeaderUpper, (s, v) => s.Theme.HeaderUpper = v);
        yield return Bool(A, "Style", "Bold headers", s => s.Theme.HeaderBold, (s, v) => s.Theme.HeaderBold = v);
        yield return Bool(A, "Style", "Bold values", s => s.Theme.BoldValues, (s, v) => s.Theme.BoldValues = v);
        yield return Choice(A, "Style", "Usage bars", ThemeStore.BarStyles, s => s.Theme.BarStyle, (s, t) => s.Theme.BarStyle = t);
        yield return Bool(A, "Style", "Bars along graph colours", s => s.Theme.BarGradient, (s, v) => s.Theme.BarGradient = v);
        yield return Choice(A, "Style", "Graph colours", ThemeStore.GraphColorings, s => s.Theme.GraphColoring, (s, t) => s.Theme.GraphColoring = t);
        yield return new SettingField
        {
            Page = A, Section = "Style", Label = "Graph colour stops (low → high)", Kind = FieldKind.Text,
            Get = s => string.Join(" ", s.Theme.GraphStops),
            Set = (s, t) =>
            {
                var parts = t.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return false;
                var list = new List<string>();
                foreach (var p in parts)
                {
                    if (!Rgb.TryParse(p, out var c)) return false;
                    list.Add(c.ToHex());
                }
                s.Theme.GraphStops = list;
                return true;
            },
        };

        const string C = "Colours";
        yield return Color(A, C, "Text and values", s => s.Theme.Text, (s, v) => s.Theme.Text = v);
        yield return Color(A, C, "Labels", s => s.Theme.Label, (s, v) => s.Theme.Label = v);
        yield return Color(A, C, "Units", s => s.Theme.Unit, (s, v) => s.Theme.Unit = v);
        yield return Color(A, C, "Dim", s => s.Theme.Dim, (s, v) => s.Theme.Dim = v);
        yield return Color(A, C, "Headers", s => s.Theme.Header, (s, v) => s.Theme.Header = v);
        yield return Color(A, C, "Rules", s => s.Theme.Rule, (s, v) => s.Theme.Rule = v);
        yield return Color(A, C, "Icons", s => s.Theme.Icon, (s, v) => s.Theme.Icon = v);
        yield return Color(A, C, "Warning", s => s.Theme.Warn, (s, v) => s.Theme.Warn = v);
        yield return Color(A, C, "Critical", s => s.Theme.Crit, (s, v) => s.Theme.Crit = v);
        yield return Color(A, C, "CPU", s => s.Theme.Cpu, (s, v) => s.Theme.Cpu = v);
        yield return Color(A, C, "Network down", s => s.Theme.NetDown, (s, v) => s.Theme.NetDown = v);
        yield return Color(A, C, "Network up", s => s.Theme.NetUp, (s, v) => s.Theme.NetUp = v);
        yield return Color(A, C, "GPU", s => s.Theme.Gpu, (s, v) => s.Theme.Gpu = v);
        yield return Color(A, C, "Memory", s => s.Theme.Memory, (s, v) => s.Theme.Memory = v);
        yield return Color(A, C, "Disk", s => s.Theme.Disk, (s, v) => s.Theme.Disk = v);
        yield return Color(A, C, "Claude", s => s.Theme.Claude, (s, v) => s.Theme.Claude = v);
        yield return Color(A, C, "Codex", s => s.Theme.Codex, (s, v) => s.Theme.Codex = v);
        yield return Color(A, C, "Copilot", s => s.Theme.Copilot, (s, v) => s.Theme.Copilot = v);
        yield return Color(A, C, "Devices", s => s.Theme.Device, (s, v) => s.Theme.Device = v);
        yield return Color(A, C, "Process names", s => s.Theme.Process, (s, v) => s.Theme.Process = v);

        // ── Thresholds ──
        const string T = "Thresholds";
        yield return Number(T, "", "Hot core: load % (smoothed)", 0, 100, s => s.Thresholds.HotCoreLoad, (s, v) => s.Thresholds.HotCoreLoad = v);
        yield return Number(T, "", "Hot core: °C above median core", 0, 100, s => s.Thresholds.HotCoreTempDelta, (s, v) => s.Thresholds.HotCoreTempDelta = v);
        yield return Number(T, "", "Core outlier min σ (points)", 0, 100, s => s.Thresholds.CoreMinSigma, (s, v) => s.Thresholds.CoreMinSigma = v);
        yield return Number(T, "", "Core outlier factor (× σ)", 0, 10, s => s.Thresholds.CoreSigmaFactor, (s, v) => s.Thresholds.CoreSigmaFactor = v);
        foreach (var f in Pair(T, "CPU temperature °C", 0, 150, s => s.Thresholds.CpuTemp)) yield return f;
        yield return Number(T, "", "Throttling: performance below %", 0, 200, s => s.Thresholds.ThrottlePerf, (s, v) => s.Thresholds.ThrottlePerf = v);
        yield return Number(T, "", "Throttling: min load %", 0, 100, s => s.Thresholds.ThrottleMinLoad, (s, v) => s.Thresholds.ThrottleMinLoad = v);
        yield return Number(T, "", "CPU top process: load %", 0, 100, s => s.Thresholds.CpuTopProcessLoad, (s, v) => s.Thresholds.CpuTopProcessLoad = v);
        yield return Number(T, "", "GPU load %", 0, 100, s => s.Thresholds.GpuLoad, (s, v) => s.Thresholds.GpuLoad = v);
        foreach (var f in Pair(T, "GPU temperature °C", 0, 150, s => s.Thresholds.GpuTemp)) yield return f;
        foreach (var f in Pair(T, "VRAM used %", 0, 100, s => s.Thresholds.Vram)) yield return f;
        yield return Number(T, "", "GPU top process: load %", 0, 100, s => s.Thresholds.GpuTopProcessLoad, (s, v) => s.Thresholds.GpuTopProcessLoad = v);
        foreach (var f in Pair(T, "Available RAM %", 0, 100, s => s.Thresholds.AvailableRam)) yield return f;
        foreach (var f in Pair(T, "Commit charge %", 0, 100, s => s.Thresholds.Commit)) yield return f;
        foreach (var f in Pair(T, "Hard page faults /s", 0, 1e7, s => s.Thresholds.PageFaults)) yield return f;
        foreach (var f in Pair(T, "Soft page faults /s", 0, 1e8, s => s.Thresholds.SoftPageFaults)) yield return f;
        foreach (var f in Pair(T, "Disk active %", 0, 100, s => s.Thresholds.DiskActive)) yield return f;
        foreach (var f in Pair(T, "Disk queue length", 0, 1000, s => s.Thresholds.DiskQueue)) yield return f;
        foreach (var f in Pair(T, "Free space %", 0, 100, s => s.Thresholds.FreeSpacePercent)) yield return f;
        yield return Number(T, "", "Free space warning GB", 0, 1e6, s => s.Thresholds.FreeSpaceGb, (s, v) => s.Thresholds.FreeSpaceGb = v);
        foreach (var f in Pair(T, "Packet loss %", 0, 100, s => s.Thresholds.PacketLoss)) yield return f;
        foreach (var f in Pair(T, "Gateway latency ms", 0, 10000, s => s.Thresholds.GatewayLatency)) yield return f;
        foreach (var f in Pair(T, "Internet latency ms", 0, 10000, s => s.Thresholds.InternetLatency)) yield return f;
        foreach (var f in Pair(T, "Claude/Codex/Copilot: reset ÷ limit time", 0, 100, s => s.Thresholds.ClaudeProjection)) yield return f;
        foreach (var f in Pair(T, "Device battery %", 0, 100, s => s.Thresholds.Battery)) yield return f;

        // ── Intervals ──
        const string V = "Intervals";
        yield return Number(V, "", "Sensors (s)", 0.25, 60, s => s.Intervals.SensorSeconds, (s, v) => s.Intervals.SensorSeconds = v);
        yield return Number(V, "", "Claude usage (s)", 60, 3600, s => s.Intervals.ClaudeSeconds, (s, v) => s.Intervals.ClaudeSeconds = v);
        yield return Number(V, "", "Codex usage (s)", 60, 3600, s => s.Intervals.CodexSeconds, (s, v) => s.Intervals.CodexSeconds = v);
        yield return Number(V, "", "Copilot usage (s)", 60, 3600, s => s.Intervals.CopilotSeconds, (s, v) => s.Intervals.CopilotSeconds = v);
        yield return Number(V, "", "Device batteries (s)", 10, 3600, s => s.Intervals.BatterySeconds, (s, v) => s.Intervals.BatterySeconds = v);
        yield return Number(V, "", "Show delay (s)", 0, 600, s => s.Intervals.ShowDelaySeconds, (s, v) => s.Intervals.ShowDelaySeconds = v);
        yield return Number(V, "", "Hide delay (s)", 0, 600, s => s.Intervals.HideDelaySeconds, (s, v) => s.Intervals.HideDelaySeconds = v);

        // ── Network ──
        const string N = "Network";
        yield return new SettingField
        {
            Page = N, Label = "Excluded adapters (one per line)", Kind = FieldKind.List,
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
            Page = N, Label = "Internet ping host", Kind = FieldKind.Text,
            Get = s => s.Network.InternetHost,
            Set = (s, t) =>
            {
                t = t.Trim();
                if (t.Length == 0 || t.Contains(' ')) return false;
                s.Network.InternetHost = t;
                return true;
            },
        };

        // ── Notes ──
        yield return new SettingField
        {
            Page = "Notes", Label = "New note hotkey", Kind = FieldKind.Text,
            Get = s => s.Notes.NewNoteHotkey,
            Set = (s, t) =>
            {
                t = t.Trim();
                if (t.Length > 0 && !Util.Hotkey.TryParse(t, out _, out _)) return false;
                s.Notes.NewNoteHotkey = t;
                return true;
            },
        };
        yield return Slider("Notes", "", "Note opacity %", 30, 100, s => s.Notes.Opacity, (s, v) => s.Notes.Opacity = (int)v);
    }
}
