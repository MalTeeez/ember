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

    /// <summary>Header style: rule, spaced, plain, bracket, dots, fade, double, heavy, box, angle, wave, diamond, arrow, chevron, blocks.</summary>
    public string HeaderStyle { get; set; } = "rule";
    /// <summary>Header title position: left, center, right.</summary>
    public string HeaderAlign { get; set; } = "left";
    /// <summary>Rule characters before a left title (after a right title); 0 = title at the edge.</summary>
    public int HeaderIndent { get; set; } = 2;
    /// <summary>An icon for the section before the header title.</summary>
    public bool HeaderIcons { get; set; } = false;
    /// <summary>Text shadow (when the background has one) also on section headers.</summary>
    public bool HeaderTextShadow { get; set; } = true;
    /// <summary>Rounding of value chips and tracks: 0 (square) to 100 (fully round ends), in %.</summary>
    public int ChipRadius { get; set; } = 40;
    public bool HeaderUpper { get; set; } = false;
    public bool HeaderBold { get; set; } = true;
    public bool BoldValues { get; set; } = true;

    public BackgroundTheme Background { get; set; } = new();

    // Backgrounds of rows and parts of rows (all off by default).
    /// <summary>Every second line of a section gets a faint band.</summary>
    public bool RowStripes { get; set; } = false;
    /// <summary>The toolbar (top line) gets a lighter band.</summary>
    public bool ToolbarBand { get; set; } = false;
    /// <summary>Section headers get a band in the header colour.</summary>
    public bool HeaderBand { get; set; } = false;
    /// <summary>Graphs get a dim track behind their cells.</summary>
    public bool GraphTrack { get; set; } = false;
    /// <summary>Usage and battery bars get a dim track behind their cells.</summary>
    public bool BarTrack { get; set; } = false;
    /// <summary>Values in warning/critical colour get a tinted chip behind them.</summary>
    public bool ValueChips { get; set; } = false;
    /// <summary>Process lines get a band in the process colour.</summary>
    public bool ProcessBand { get; set; } = false;
    /// <summary>Strength of all these backgrounds in % (100 = default strength).</summary>
    public int BandOpacity { get; set; } = 100;

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
    /// <summary>0..100: how strong the gradient (or glass highlight) is; 0 = none.</summary>
    public int GradientStrength { get; set; } = 100;
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

    /// <summary>A background of the colour; <paramref name="alpha"/> is scaled by the theme's band strength.</summary>
    public Shade Band(Rgb color, int alpha) => new(color, (byte)Math.Clamp(alpha * Theme.BandOpacity / 100, 0, 255));

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
            t.BandOpacity = Math.Clamp(t.BandOpacity, 0, 300);
            t.ChipRadius = Math.Clamp(t.ChipRadius, 0, 100);
            return t;
        }
        catch (JsonException) { return null; }
    }

    public static readonly string[] BackgroundKinds = { "none", "solid", "gradient", "acrylic", "blur", "panel", "glass" };
    public static readonly string[] HeaderStyles = { "rule", "spaced", "plain", "bracket", "dots", "fade", "double", "heavy", "box", "angle", "wave", "diamond", "arrow", "chevron", "blocks" };
    public static readonly string[] HeaderAligns = { "left", "center", "right" };
    public static readonly string[] BarStyles = { "colons", "squares", "pips", "braille", "line", "shade", "blocks" };
    public static readonly string[] GraphColorings = { "intensity", "category", "mono" };

    public static IReadOnlyList<Theme> BuiltIn { get; } = new[]
    {
        Ember(), Btop(), Mono(), Nord(), Dusk(), Glass(),
        Gruvbox(), Dracula(), TokyoNight(), Mocha(), Latte(), Solarized(), Everforest(), RosePine(),
        Kanagawa(), Monokai(), OneDark(), Matrix(), Synthwave(), Paper(),
    };

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

    /// <summary>Gruvbox dark: warm panel with a border, bracket headers with icons, block bars.</summary>
    public static Theme Gruvbox() => new()
    {
        Name = "gruvbox",
        Text = "#EBDBB2", Label = "#A89984", Unit = "#928374", Dim = "#504945", Header = "#FABD2F", Rule = "#665C54", Icon = "#A89984",
        Warn = "#FE8019", Crit = "#FB4934",
        Cpu = "#B8BB26", NetDown = "#8EC07C", NetUp = "#D3869B", Gpu = "#83A598", Memory = "#FABD2F", Disk = "#FE8019",
        Claude = "#FE8019", Codex = "#83A598", Copilot = "#D3869B", Device = "#B8BB26", Process = "#EBDBB2",
        GraphColoring = "intensity", GraphStops = new() { "#98971A", "#D79921", "#CC241D" },
        BarStyle = "blocks", HeaderStyle = "bracket", HeaderIcons = true,
        Background = new BackgroundTheme { Kind = "panel", Color = "#282828", Opacity = 92, CornerRadius = 4, Border = "#665C54", BorderOpacity = 100, Padding = 10 },
    };

    /// <summary>Dracula: purple-grey gradient, pink headers, value chips for warnings.</summary>
    public static Theme Dracula() => new()
    {
        Name = "dracula",
        Text = "#F8F8F2", Label = "#BD93F9", Unit = "#6272A4", Dim = "#44475A", Header = "#FF79C6", Rule = "#44475A", Icon = "#BD93F9",
        Warn = "#FFB86C", Crit = "#FF5555",
        Cpu = "#50FA7B", NetDown = "#8BE9FD", NetUp = "#FF79C6", Gpu = "#BD93F9", Memory = "#F1FA8C", Disk = "#FFB86C",
        Claude = "#FFB86C", Codex = "#8BE9FD", Copilot = "#BD93F9", Device = "#50FA7B", Process = "#F8F8F2",
        GraphColoring = "intensity", GraphStops = new() { "#50FA7B", "#F1FA8C", "#FF5555" },
        BarStyle = "pips", HeaderStyle = "double", ValueChips = true,
        Background = new BackgroundTheme { Kind = "gradient", Color = "#282A36", Color2 = "#1E1F29", Opacity = 90, CornerRadius = 12, Border = "#6272A4", BorderOpacity = 30, GradientAngle = 90, Padding = 12 },
    };

    /// <summary>Tokyo Night: deep blue acrylic, centred headers with icons, graph tracks.</summary>
    public static Theme TokyoNight() => new()
    {
        Name = "tokyo-night",
        Text = "#C0CAF5", Label = "#7AA2F7", Unit = "#565F89", Dim = "#3B4261", Header = "#BB9AF7", Rule = "#3B4261", Icon = "#7AA2F7",
        Warn = "#E0AF68", Crit = "#F7768E",
        Cpu = "#7DCFFF", NetDown = "#9ECE6A", NetUp = "#FF9E64", Gpu = "#BB9AF7", Memory = "#7AA2F7", Disk = "#E0AF68",
        Claude = "#FF9E64", Codex = "#7DCFFF", Copilot = "#BB9AF7", Device = "#9ECE6A", Process = "#C0CAF5",
        GraphColoring = "intensity", GraphStops = new() { "#7AA2F7", "#BB9AF7", "#F7768E" },
        BarStyle = "line", BarGradient = true, HeaderStyle = "angle", HeaderAlign = "center", HeaderIcons = true, GraphTrack = true,
        Background = new BackgroundTheme { Kind = "acrylic", Color = "#1A1B26", Opacity = 70, CornerRadius = 14, Border = "#7AA2F7", BorderOpacity = 18, Padding = 12 },
    };

    /// <summary>Catppuccin Mocha: soft pastel on blur, row stripes, rounded.</summary>
    public static Theme Mocha() => new()
    {
        Name = "catppuccin",
        Text = "#CDD6F4", Label = "#B4BEFE", Unit = "#7F849C", Dim = "#45475A", Header = "#CBA6F7", Rule = "#45475A", Icon = "#B4BEFE",
        Warn = "#F9E2AF", Crit = "#F38BA8",
        Cpu = "#FAB387", NetDown = "#94E2D5", NetUp = "#F5C2E7", Gpu = "#CBA6F7", Memory = "#89B4FA", Disk = "#F9E2AF",
        Claude = "#FAB387", Codex = "#89DCEB", Copilot = "#CBA6F7", Device = "#A6E3A1", Process = "#CDD6F4",
        GraphColoring = "intensity", GraphStops = new() { "#A6E3A1", "#F9E2AF", "#F38BA8" },
        BarStyle = "squares", HeaderStyle = "plain", HeaderIndent = 0, HeaderIcons = true, RowStripes = true,
        Background = new BackgroundTheme { Kind = "blur", Color = "#1E1E2E", Opacity = 78, CornerRadius = 18, Border = "#B4BEFE", BorderOpacity = 14, Padding = 14 },
    };

    /// <summary>Catppuccin Latte: light theme for light wallpapers.</summary>
    public static Theme Latte() => new()
    {
        Name = "latte",
        Text = "#4C4F69", Label = "#7287FD", Unit = "#8C8FA1", Dim = "#BCC0CC", Header = "#8839EF", Rule = "#BCC0CC", Icon = "#7287FD",
        Warn = "#DF8E1D", Crit = "#D20F39",
        Cpu = "#FE640B", NetDown = "#179299", NetUp = "#EA76CB", Gpu = "#8839EF", Memory = "#1E66F5", Disk = "#DF8E1D",
        Claude = "#FE640B", Codex = "#04A5E5", Copilot = "#8839EF", Device = "#40A02B", Process = "#4C4F69",
        GraphColoring = "intensity", GraphStops = new() { "#40A02B", "#DF8E1D", "#D20F39" },
        BarStyle = "blocks", HeaderStyle = "rule", HeaderBand = true, BoldValues = true,
        Background = new BackgroundTheme { Kind = "solid", Color = "#EFF1F5", Opacity = 94, CornerRadius = 12, Border = "#BCC0CC", BorderOpacity = 80, Padding = 12 },
    };

    /// <summary>Solarized dark: flat panel, right-aligned headers.</summary>
    public static Theme Solarized() => new()
    {
        Name = "solarized",
        Text = "#EEE8D5", Label = "#93A1A1", Unit = "#657B83", Dim = "#2F4A54", Header = "#B58900", Rule = "#2F4A54", Icon = "#93A1A1",
        Warn = "#CB4B16", Crit = "#DC322F",
        Cpu = "#859900", NetDown = "#2AA198", NetUp = "#D33682", Gpu = "#6C71C4", Memory = "#268BD2", Disk = "#B58900",
        Claude = "#CB4B16", Codex = "#268BD2", Copilot = "#6C71C4", Device = "#859900", Process = "#EEE8D5",
        GraphColoring = "category", GraphStops = new() { "#859900", "#B58900", "#DC322F" },
        BarStyle = "shade", HeaderStyle = "rule", HeaderAlign = "right",
        Background = new BackgroundTheme { Kind = "panel", Color = "#002B36", Opacity = 90, CornerRadius = 0, Border = "#073642", BorderOpacity = 100, Padding = 10 },
    };

    /// <summary>Everforest: green-grey solid, header bands, process bands.</summary>
    public static Theme Everforest() => new()
    {
        Name = "everforest",
        Text = "#D3C6AA", Label = "#A7C080", Unit = "#859289", Dim = "#475258", Header = "#A7C080", Rule = "#475258", Icon = "#A7C080",
        Warn = "#DBBC7F", Crit = "#E67E80",
        Cpu = "#A7C080", NetDown = "#83C092", NetUp = "#D699B6", Gpu = "#7FBBB3", Memory = "#7FBBB3", Disk = "#DBBC7F",
        Claude = "#E69875", Codex = "#7FBBB3", Copilot = "#D699B6", Device = "#A7C080", Process = "#D3C6AA",
        GraphColoring = "intensity", GraphStops = new() { "#A7C080", "#DBBC7F", "#E67E80" },
        BarStyle = "colons", HeaderStyle = "plain", HeaderIndent = 1, HeaderBand = true, ProcessBand = true,
        Background = new BackgroundTheme { Kind = "solid", Color = "#2D353B", Opacity = 88, CornerRadius = 10, BorderOpacity = 0, Padding = 12 },
    };

    /// <summary>Rosé Pine: muted glass with gold and rose.</summary>
    public static Theme RosePine() => new()
    {
        Name = "rose-pine",
        Text = "#E0DEF4", Label = "#C4A7E7", Unit = "#908CAA", Dim = "#403D52", Header = "#EBBCBA", Rule = "#403D52", Icon = "#C4A7E7",
        Warn = "#F6C177", Crit = "#EB6F92",
        Cpu = "#EBBCBA", NetDown = "#9CCFD8", NetUp = "#F6C177", Gpu = "#C4A7E7", Memory = "#31748F", Disk = "#F6C177",
        Claude = "#EBBCBA", Codex = "#9CCFD8", Copilot = "#C4A7E7", Device = "#9CCFD8", Process = "#E0DEF4",
        GraphColoring = "intensity", GraphStops = new() { "#31748F", "#F6C177", "#EB6F92" },
        BarStyle = "braille", HeaderStyle = "diamond", HeaderIndent = 0, HeaderIcons = true,
        Background = new BackgroundTheme { Kind = "glass", Color = "#191724", Color2 = "#EBBCBA", Opacity = 55, CornerRadius = 16, Border = "#EBBCBA", BorderOpacity = 16, Padding = 12, TextShadow = true },
    };

    /// <summary>Kanagawa: ink-blue gradient with wave colours, bar tracks.</summary>
    public static Theme Kanagawa() => new()
    {
        Name = "kanagawa",
        Text = "#DCD7BA", Label = "#7E9CD8", Unit = "#727169", Dim = "#363646", Header = "#E6C384", Rule = "#363646", Icon = "#7E9CD8",
        Warn = "#FFA066", Crit = "#E82424",
        Cpu = "#98BB6C", NetDown = "#7FB4CA", NetUp = "#D27E99", Gpu = "#957FB8", Memory = "#7E9CD8", Disk = "#E6C384",
        Claude = "#FFA066", Codex = "#7FB4CA", Copilot = "#957FB8", Device = "#98BB6C", Process = "#DCD7BA",
        GraphColoring = "intensity", GraphStops = new() { "#7E9CD8", "#E6C384", "#E82424" },
        BarStyle = "squares", HeaderStyle = "wave", BarTrack = true,
        Background = new BackgroundTheme { Kind = "gradient", Color = "#1F1F28", Color2 = "#16161D", Opacity = 88, CornerRadius = 8, Border = "#54546D", BorderOpacity = 50, GradientAngle = 135, Padding = 12 },
    };

    /// <summary>Monokai: black panel, vivid accents, chips and graph tracks.</summary>
    public static Theme Monokai() => new()
    {
        Name = "monokai",
        Text = "#F8F8F2", Label = "#A6E22E", Unit = "#75715E", Dim = "#3E3D32", Header = "#F92672", Rule = "#49483E", Icon = "#66D9EF",
        Warn = "#FD971F", Crit = "#F92672",
        Cpu = "#A6E22E", NetDown = "#66D9EF", NetUp = "#AE81FF", Gpu = "#AE81FF", Memory = "#66D9EF", Disk = "#E6DB74",
        Claude = "#FD971F", Codex = "#66D9EF", Copilot = "#AE81FF", Device = "#A6E22E", Process = "#F8F8F2",
        GraphColoring = "intensity", GraphStops = new() { "#A6E22E", "#E6DB74", "#F92672" },
        BarStyle = "blocks", BarGradient = true, HeaderStyle = "box", HeaderUpper = true, ValueChips = true, GraphTrack = true,
        Background = new BackgroundTheme { Kind = "panel", Color = "#272822", Opacity = 95, CornerRadius = 6, Border = "#49483E", BorderOpacity = 100, Padding = 10 },
    };

    /// <summary>One Dark: grey-blue acrylic, plain icons headers, stripes.</summary>
    public static Theme OneDark() => new()
    {
        Name = "one-dark",
        Text = "#ABB2BF", Label = "#61AFEF", Unit = "#5C6370", Dim = "#3E4451", Header = "#E5C07B", Rule = "#3E4451", Icon = "#61AFEF",
        Warn = "#D19A66", Crit = "#E06C75",
        Cpu = "#98C379", NetDown = "#56B6C2", NetUp = "#C678DD", Gpu = "#C678DD", Memory = "#61AFEF", Disk = "#E5C07B",
        Claude = "#D19A66", Codex = "#56B6C2", Copilot = "#C678DD", Device = "#98C379", Process = "#ABB2BF",
        GraphColoring = "intensity", GraphStops = new() { "#98C379", "#E5C07B", "#E06C75" },
        BarStyle = "line", HeaderStyle = "dots", HeaderIndent = 0, HeaderIcons = true, RowStripes = true,
        Background = new BackgroundTheme { Kind = "acrylic", Color = "#282C34", Opacity = 75, CornerRadius = 10, Border = "#ABB2BF", BorderOpacity = 10, Padding = 12 },
    };

    /// <summary>Matrix: green on black glass, upper-case spaced headers, text shadow.</summary>
    public static Theme Matrix() => new()
    {
        Name = "matrix",
        Text = "#B8FFB8", Label = "#3DDC3D", Unit = "#2A8F2A", Dim = "#164016", Header = "#7CFF7C", Rule = "#1E5A1E", Icon = "#3DDC3D",
        Warn = "#E8FF4D", Crit = "#FF4D4D",
        Cpu = "#3DDC3D", NetDown = "#3DDC3D", NetUp = "#3DDC3D", Gpu = "#3DDC3D", Memory = "#3DDC3D", Disk = "#3DDC3D",
        Claude = "#7CFF7C", Codex = "#7CFF7C", Copilot = "#7CFF7C", Device = "#3DDC3D", Process = "#B8FFB8",
        GraphColoring = "intensity", GraphStops = new() { "#145214", "#3DDC3D", "#B8FFB8" },
        BarStyle = "braille", HeaderStyle = "spaced", HeaderIndent = 1, HeaderUpper = true, HeaderBold = false, GraphTrack = true,
        Background = new BackgroundTheme { Kind = "solid", Color = "#000000", Opacity = 80, CornerRadius = 2, Border = "#3DDC3D", BorderOpacity = 35, Padding = 10, TextShadow = true },
    };

    /// <summary>Synthwave: neon on a purple gradient, centred fade headers, header bands.</summary>
    public static Theme Synthwave() => new()
    {
        Name = "synthwave",
        Text = "#FFFFFF", Label = "#FF7EDB", Unit = "#B893CE", Dim = "#4A3466", Header = "#FEDE5D", Rule = "#72F1B8", Icon = "#36F9F6",
        Warn = "#FEDE5D", Crit = "#FE4450",
        Cpu = "#FF7EDB", NetDown = "#36F9F6", NetUp = "#FF8B39", Gpu = "#B893CE", Memory = "#36F9F6", Disk = "#FEDE5D",
        Claude = "#FF8B39", Codex = "#36F9F6", Copilot = "#FF7EDB", Device = "#72F1B8", Process = "#FFFFFF",
        GraphColoring = "intensity", GraphStops = new() { "#36F9F6", "#FF7EDB", "#FE4450" },
        BarStyle = "line", BarGradient = true, HeaderStyle = "fade", HeaderAlign = "center", HeaderBand = true,
        Background = new BackgroundTheme { Kind = "gradient", Color = "#2B213A", Color2 = "#0D0221", Opacity = 90, CornerRadius = 16, Border = "#FF7EDB", BorderOpacity = 35, GradientAngle = 90, Padding = 12, TextShadow = true },
    };

    /// <summary>Paper: black on white, square, like a printed page.</summary>
    public static Theme Paper() => new()
    {
        Name = "paper",
        Text = "#1A1A1A", Label = "#444444", Unit = "#777777", Dim = "#C8C8C8", Header = "#000000", Rule = "#999999", Icon = "#444444",
        Warn = "#B36B00", Crit = "#C0161D",
        Cpu = "#1A1A1A", NetDown = "#1A1A1A", NetUp = "#1A1A1A", Gpu = "#1A1A1A", Memory = "#1A1A1A", Disk = "#1A1A1A",
        Claude = "#1A1A1A", Codex = "#1A1A1A", Copilot = "#1A1A1A", Device = "#1A1A1A", Process = "#1A1A1A",
        GraphColoring = "mono", GraphStops = new() { "#1A1A1A" },
        BarStyle = "shade", HeaderStyle = "rule", HeaderUpper = true, ValueChips = true,
        Background = new BackgroundTheme { Kind = "solid", Color = "#FAFAF7", Opacity = 96, CornerRadius = 0, Border = "#1A1A1A", BorderOpacity = 100, Padding = 10 },
    };
}
