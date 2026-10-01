using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ember.Core.Render;

/// <summary>
/// The look of the widget and the notes: colours (hex text, so themes are easy to edit as JSON),
/// header, bar and graph styles, and the background. Themes can be exported and imported as JSON.
/// </summary>
public sealed class Theme
{
    public string Name { get; set; } = "ember";

    // Text roles
    public string Text { get; set; } = "#E8E3DC";      // normal text and values
    public string Label { get; set; } = "#A0958A";     // item labels
    public string Unit { get; set; } = "#7D746C";      // units, small words
    public string Dim { get; set; } = "#5E5751";       // leaders, empty bar cells, quiet text
    public string Header { get; set; } = "#D8CFC6";    // section titles
    public string Rule { get; set; } = "#4A433D";      // header rules, spacers
    public string Icon { get; set; } = "#A0958A";      // icons and buttons
    public string Warn { get; set; } = "#FFB000";
    public string Crit { get; set; } = "#FF453A";

    // Accent per category (labels, graphs in "category" mode, bars)
    public string Cpu { get; set; } = "#FF9E5E";
    public string NetDown { get; set; } = "#7FD1B9";
    public string NetUp { get; set; } = "#F2A65A";
    public string Gpu { get; set; } = "#C3A6FF";
    public string Memory { get; set; } = "#8CC8FF";
    public string Disk { get; set; } = "#F7D06B";
    public string Claude { get; set; } = "#E8A27C";
    public string Codex { get; set; } = "#8FB8FF";
    public string Copilot { get; set; } = "#B79CFF";
    public string Device { get; set; } = "#9FD6A0";
    public string Process { get; set; } = "#D8CFC6";

    /// <summary>"intensity" (each graph cell coloured by its value, btop style), "category" (accent colour), "mono" (text colour).</summary>
    public string GraphColoring { get; set; } = "intensity";
    /// <summary>Low → high colours for "intensity" graphs and bars.</summary>
    public List<string> GraphStops { get; set; } = new() { "#6B8F71", "#E0B050", "#FF5A3C" };

    /// <summary>Usage bar style: blocks, colons, squares, pips, braille, line, shade.</summary>
    public string BarStyle { get; set; } = "colons";
    /// <summary>Filled bar cells coloured along the graph stops (by position) instead of the category accent.</summary>
    public bool BarGradient { get; set; } = false;

    /// <summary>Header style: rule, spaced, plain, bracket, dots, fade.</summary>
    public string HeaderStyle { get; set; } = "rule";
    /// <summary>Header title position: left, center, right.</summary>
    public string HeaderAlign { get; set; } = "left";
    /// <summary>Rule characters before a left title (after a right title); 0 = title at the edge.</summary>
    public int HeaderIndent { get; set; } = 2;
    public bool HeaderUpper { get; set; } = false;
    public bool HeaderBold { get; set; } = true;
    public bool BoldValues { get; set; } = true;

    public BackgroundTheme Background { get; set; } = new();

    public Theme Clone() => ThemeStore.Parse(ThemeStore.Serialize(this)) ?? new Theme();
}

/// <summary>Background of the widget and the notes.</summary>
public sealed class BackgroundTheme
{
    /// <summary>none, solid, gradient, acrylic, blur, panel, glass.</summary>
    public string Kind { get; set; } = "acrylic";
    public string Color { get; set; } = "#14100D";
    /// <summary>Second colour (gradient end, glass highlight).</summary>
    public string Color2 { get; set; } = "#000000";
    /// <summary>0..100: opacity of the background colour (the text keeps its own opacity).</summary>
    public int Opacity { get; set; } = 55;
    public int CornerRadius { get; set; } = 12;
    public string Border { get; set; } = "#FFFFFF";
    /// <summary>0..100. 0 = no border.</summary>
    public int BorderOpacity { get; set; } = 10;
    /// <summary>Inner space in DIP.</summary>
    public int Padding { get; set; } = 10;
    /// <summary>Gradient direction in degrees (90 = top to bottom).</summary>
    public int GradientAngle { get; set; } = 90;
    /// <summary>Dark shadow under each glyph (helps on bright wallpapers without a background).</summary>
    public bool TextShadow { get; set; } = false;
}

/// <summary>Colours of a theme, parsed once.</summary>
public sealed class ThemeColors
{
    public Rgb Text, Label, Unit, Dim, Header, Rule, Icon, Warn, Crit;
    public Rgb Cpu, NetDown, NetUp, Gpu, Memory, Disk, Claude, Codex, Copilot, Device, Process;
    public IReadOnlyList<Rgb> GraphStops = Array.Empty<Rgb>();
    public Theme Theme = new();

    public static ThemeColors From(Theme t)
    {
        Rgb P(string s, Rgb f) => Rgb.Parse(s, f);
        var c = new ThemeColors
        {
            Theme = t,
            Text = P(t.Text, Palette.Bright),
            Label = P(t.Label, Palette.Dim),
            Unit = P(t.Unit, Palette.Dim),
            Dim = P(t.Dim, Palette.Dim),
            Header = P(t.Header, Palette.Bright),
            Rule = P(t.Rule, Palette.Dim),
            Icon = P(t.Icon, Palette.Dim),
            Warn = P(t.Warn, Palette.Amber),
            Crit = P(t.Crit, Palette.Red),
        };
        c.Cpu = P(t.Cpu, c.Text);
        c.NetDown = P(t.NetDown, c.Text);
        c.NetUp = P(t.NetUp, c.Text);
        c.Gpu = P(t.Gpu, c.Text);
        c.Memory = P(t.Memory, c.Text);
        c.Disk = P(t.Disk, c.Text);
        c.Claude = P(t.Claude, c.Text);
        c.Codex = P(t.Codex, c.Text);
        c.Copilot = P(t.Copilot, c.Text);
        c.Device = P(t.Device, c.Text);
        c.Process = P(t.Process, c.Text);
        var stops = (t.GraphStops ?? new()).Select(s => P(s, c.Text)).ToList();
        if (stops.Count == 0) stops.Add(c.Text);
        c.GraphStops = stops;
        return c;
    }

    /// <summary>Threshold colour: normal → warning → critical.</summary>
    public Rgb Severity(double value, double warn, double crit, Rgb? normal = null)
        => Gradient.Color(Gradient.Severity(value, warn, crit), normal ?? Text, Warn, Crit);

    public Rgb SeverityOf(double severity, Rgb? normal = null) => Gradient.Color(severity, normal ?? Text, Warn, Crit);

    /// <summary>Colour of one graph cell.</summary>
    public Rgb GraphCell(double fraction, Rgb accent)
        => Theme.GraphColoring switch
        {
            "mono" => Text,
            "category" => accent,
            _ => Rgb.Stops(GraphStops, fraction),
        };
}

/// <summary>Built-in themes and JSON import/export.</summary>
public static class ThemeStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string Serialize(Theme t) => JsonSerializer.Serialize(t, Options);

    public static Theme? Parse(string json)
    {
        try
        {
            var t = JsonSerializer.Deserialize<Theme>(json, Options);
            if (t == null) return null;
            t.Background ??= new BackgroundTheme();
            t.GraphStops ??= new();
            t.HeaderAlign = HeaderAligns.Contains(t.HeaderAlign) ? t.HeaderAlign : "left";
            t.HeaderIndent = Math.Clamp(t.HeaderIndent, 0, 24);
            return t;
        }
        catch (JsonException) { return null; }
    }

    public static readonly string[] BackgroundKinds = { "none", "solid", "gradient", "acrylic", "blur", "panel", "glass" };
    public static readonly string[] HeaderStyles = { "rule", "spaced", "plain", "bracket", "dots", "fade" };
    public static readonly string[] HeaderAligns = { "left", "center", "right" };
    public static readonly string[] BarStyles = { "colons", "squares", "pips", "braille", "line", "shade", "blocks" };
    public static readonly string[] GraphColorings = { "intensity", "category", "mono" };

    public static IReadOnlyList<Theme> BuiltIn { get; } = new[] { Ember(), Btop(), Mono(), Nord(), Dusk(), Glass() };

    public static Theme? Find(string name) => BuiltIn.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Clone();

    /// <summary>Warm, dark acrylic. Default.</summary>
    public static Theme Ember() => new();

    /// <summary>btop-like: green → yellow → red graphs, square bars, bordered panel.</summary>
    public static Theme Btop() => new()
    {
        Name = "btop",
        Text = "#EEEEEE", Label = "#B0B0B0", Unit = "#808080", Dim = "#505050", Header = "#EEEEEE", Rule = "#5A5A5A", Icon = "#A0A0A0",
        Warn = "#D7D75F", Crit = "#FF5F5F",
        Cpu = "#5FD75F", NetDown = "#5FAFFF", NetUp = "#D787D7", Gpu = "#5FD7AF", Memory = "#FFAF5F", Disk = "#AF87FF", Claude = "#D7875F", Process = "#EEEEEE",
        GraphColoring = "intensity", GraphStops = new() { "#5FD75F", "#D7D75F", "#FF5F5F" },
        BarStyle = "squares", BarGradient = true,
        HeaderStyle = "bracket",
        Background = new BackgroundTheme { Kind = "panel", Color = "#0C0C0C", Opacity = 88, CornerRadius = 6, Border = "#5A5A5A", BorderOpacity = 100, Padding = 10 },
    };

    /// <summary>The original look: monochrome text on a transparent background, with a text shadow.</summary>
    public static Theme Mono() => new()
    {
        Name = "mono",
        Text = "#E8E8E8", Label = "#8A8A8A", Unit = "#707070", Dim = "#505050", Header = "#9A9A9A", Rule = "#505050", Icon = "#8A8A8A",
        Warn = "#FFB000", Crit = "#FF453A",
        Cpu = "#E8E8E8", NetDown = "#E8E8E8", NetUp = "#E8E8E8", Gpu = "#E8E8E8", Memory = "#E8E8E8", Disk = "#E8E8E8", Claude = "#E8E8E8", Process = "#E8E8E8",
        GraphColoring = "mono", GraphStops = new() { "#E8E8E8" },
        BarStyle = "blocks", HeaderStyle = "spaced", HeaderIndent = 1, HeaderUpper = true, HeaderBold = false, BoldValues = false,
        Background = new BackgroundTheme { Kind = "none", Opacity = 0, CornerRadius = 0, BorderOpacity = 0, Padding = 6, TextShadow = true },
    };

    /// <summary>Nord colours on a blurred, cool tint.</summary>
    public static Theme Nord() => new()
    {
        Name = "nord",
        Text = "#ECEFF4", Label = "#8FBCBB", Unit = "#81A1C1", Dim = "#4C566A", Header = "#88C0D0", Rule = "#434C5E", Icon = "#81A1C1",
        Warn = "#EBCB8B", Crit = "#BF616A",
        Cpu = "#88C0D0", NetDown = "#A3BE8C", NetUp = "#B48EAD", Gpu = "#8FBCBB", Memory = "#81A1C1", Disk = "#EBCB8B", Claude = "#D08770", Process = "#E5E9F0",
        GraphColoring = "intensity", GraphStops = new() { "#5E81AC", "#88C0D0", "#EBCB8B", "#BF616A" },
        BarStyle = "pips", HeaderStyle = "plain", HeaderIndent = 0,
        Background = new BackgroundTheme { Kind = "blur", Color = "#2E3440", Opacity = 62, CornerRadius = 10, Border = "#88C0D0", BorderOpacity = 18, Padding = 10 },
    };

    /// <summary>Half-dark diagonal gradient with pink/purple accents.</summary>
    public static Theme Dusk() => new()
    {
        Name = "dusk",
        Text = "#F8F8F2", Label = "#BD93F9", Unit = "#8A7FB0", Dim = "#4D4566", Header = "#FF79C6", Rule = "#4D4566", Icon = "#BD93F9",
        Warn = "#F1FA8C", Crit = "#FF5555",
        Cpu = "#FF79C6", NetDown = "#8BE9FD", NetUp = "#50FA7B", Gpu = "#BD93F9", Memory = "#8BE9FD", Disk = "#F1FA8C", Claude = "#FFB86C", Process = "#F8F8F2",
        GraphColoring = "intensity", GraphStops = new() { "#8BE9FD", "#BD93F9", "#FF79C6" },
        BarStyle = "line", BarGradient = true, HeaderStyle = "dots", HeaderIndent = 0,
        Background = new BackgroundTheme { Kind = "gradient", Color = "#1B1530", Color2 = "#08080C", Opacity = 82, CornerRadius = 14, Border = "#BD93F9", BorderOpacity = 14, GradientAngle = 60, Padding = 12 },
    };

    /// <summary>Light glass: blur with a soft highlight from the top.</summary>
    public static Theme Glass() => new()
    {
        Name = "glass",
        Text = "#FFFFFF", Label = "#D0D6E0", Unit = "#AEB6C2", Dim = "#6C7480", Header = "#FFFFFF", Rule = "#8890A0", Icon = "#D0D6E0",
        Warn = "#FFC94D", Crit = "#FF6B6B",
        Cpu = "#7CC4FF", NetDown = "#7CF0C8", NetUp = "#FFB27C", Gpu = "#C9A7FF", Memory = "#7CC4FF", Disk = "#FFE07C", Claude = "#FFB27C", Process = "#FFFFFF",
        GraphColoring = "category", GraphStops = new() { "#7CC4FF", "#FFC94D", "#FF6B6B" },
        BarStyle = "braille", HeaderStyle = "fade", HeaderIndent = 0,
        Background = new BackgroundTheme { Kind = "glass", Color = "#1C2230", Color2 = "#FFFFFF", Opacity = 40, CornerRadius = 16, Border = "#FFFFFF", BorderOpacity = 22, Padding = 12, TextShadow = true },
    };
}
