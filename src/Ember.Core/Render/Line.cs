using System.Text;

namespace Ember.Core.Render;

[Flags]
public enum RunStyle
{
    None = 0,
    Bold = 1,
    /// <summary>Icon glyph from the icon font (Segoe Fluent Icons / Segoe MDL2 Assets).
    /// The run text is the glyph plus one filler space: an icon takes 2 cells and is centred in them.</summary>
    Icon = 2,
}

/// <summary>A background fill: colour and alpha (0..255).</summary>
public readonly record struct Shade(Rgb Color, byte Alpha)
{
    public bool Visible => Alpha > 0;
}

/// <summary>A piece of text with one colour and style, and an optional background behind its cells.</summary>
public readonly record struct Run(string Text, Rgb Color, RunStyle Style = RunStyle.None, Shade Back = default);

/// <summary>What a click on part of a line does.</summary>
public enum HitAction { None, ProcessDetails, ProcessProperties, ProcessKill, NewNote, HideWidget, RestoreNote, Drag }

/// <summary>A clickable cell range of a line. Arg = PID or note id. Tooltip shows on hover.</summary>
public readonly record struct Hit(int Col, int Width, HitAction Action, string Arg, string Tooltip);

/// <summary>Icon glyphs (code points in Segoe Fluent Icons and Segoe MDL2 Assets).</summary>
public static class Icons
{
    public const char Refresh = '';
    public const char Clock = '';
    public const char Warning = '';
    public const char Info = '';
    public const char Settings = '';
    public const char Cancel = '';
    public const char Add = '';
    public const char Note = '';
    public const char Close = '';
    public const char Minimize = '';
    public const char OpenFile = '';

    // Section icons (Segoe MDL2 Assets / Fluent Icons).
    public const char Activity = '\uE9D9';   // Diagnostic: cpu
    public const char Ethernet = '\uE839';   // net
    public const char Monitor = '\uE7F4';    // gpu
    public const char Chip = '\uE950';       // Component: memory
    public const char Drive = '\uEDA2';      // HardDrive: disk
    public const char Robot = '\uE99A';      // usage limits (claude, codex, copilot)
    public const char Battery = '\uE83F';    // devices
    public const char Tag = '\uE8EC';        // other sections

    /// <summary>Icon for a section header, by its (lower-case) name.</summary>
    public static char ForSection(string name) => name.Trim().ToLowerInvariant() switch
    {
        "cpu" or "processor" or "cores" => Activity,
        "net" or "network" or "internet" => Ethernet,
        "gpu" or "graphics" or "video" => Monitor,
        "memory" or "mem" or "ram" => Chip,
        "disk" or "disks" or "storage" or "drives" => Drive,
        "claude" or "codex" or "copilot" or "usage" or "ai" or "limits" => Robot,
        "devices" or "battery" or "batteries" => Battery,
        _ => Tag,
    };

    public static bool IsIcon(char c) => c >= '' && c <= '';
}

/// <summary>One widget line: styled runs plus clickable ranges.</summary>
public sealed class Line
{
    public List<Run> Runs { get; } = new();
    public List<Hit> Hits { get; } = new();
    /// <summary>A padding line that keeps space (layout stabilizer). Drawn empty.</summary>
    public bool IsPadding { get; init; }
    /// <summary>Background band behind the whole line (row stripes, header bands).</summary>
    public Shade Back { get; set; }

    public string Text
    {
        get
        {
            var sb = new StringBuilder();
            foreach (var r in Runs) sb.Append(r.Text);
            return sb.ToString();
        }
    }

    public int Length
    {
        get
        {
            int n = 0;
            foreach (var r in Runs) n += r.Text.Length;
            return n;
        }
    }

    /// <summary>The PID of the first process hit (0 if none).</summary>
    public int Pid => Hits.Where(h => h.Action is HitAction.ProcessDetails).Select(h => int.TryParse(h.Arg, out int p) ? p : 0).FirstOrDefault();

    /// <summary>Text, colours, styles and hits as one comparable key (used to redraw only on change).</summary>
    public string Key
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append(Back.Color.ToHex()).Append(Back.Alpha).Append('#');
            foreach (var r in Runs) sb.Append(r.Color.ToHex()).Append((int)r.Style).Append(r.Back.Color.ToHex()).Append(r.Back.Alpha).Append('|').Append(r.Text).Append('\u0001');
            foreach (var h in Hits) sb.Append(h.Col).Append(',').Append(h.Width).Append(',').Append((int)h.Action).Append(',').Append(h.Arg).Append(';');
            return sb.ToString();
        }
    }

    public override string ToString() => Text;
}

/// <summary>Builds a line of a fixed width, column by column.</summary>
public sealed class LineBuilder
{
    private readonly Line _line;
    public int Width { get; }

    public LineBuilder(int width = Layout.Width, bool padding = false)
    {
        Width = width;
        _line = new Line { IsPadding = padding };
    }

    public int Column => _line.Length;

    public LineBuilder Add(string text, Rgb color, RunStyle style = RunStyle.None, Shade back = default)
    {
        if (text.Length == 0) return this;
        var runs = _line.Runs;
        if (runs.Count > 0 && runs[^1].Color == color && runs[^1].Style == style && runs[^1].Back == back && (style & RunStyle.Icon) == 0)
            runs[^1] = new Run(runs[^1].Text + text, color, style, back);
        else
            runs.Add(new Run(text, color, style, back));
        return this;
    }

    public LineBuilder Dim(string text) => Add(text, Palette.Dim);
    public LineBuilder Bright(string text) => Add(text, Palette.Bright);

    /// <summary>An icon (2 cells).</summary>
    public LineBuilder Icon(char glyph, Rgb color) => Add(glyph + " ", color, RunStyle.Icon);

    /// <summary>A clickable icon (2 cells) with a tooltip.</summary>
    public LineBuilder Button(char glyph, Rgb color, HitAction action, string arg, string tooltip)
    {
        _line.Hits.Add(new Hit(Column, 2, action, arg, tooltip));
        return Icon(glyph, color);
    }

    /// <summary>Makes the whole line clickable (smaller hits, such as buttons, win).</summary>
    public LineBuilder LineHit(HitAction action, string arg, string tooltip)
    {
        _line.Hits.Insert(0, new Hit(0, Width, action, arg, tooltip));
        return this;
    }

    /// <summary>Pad with spaces to the column.</summary>
    public LineBuilder PadTo(int column)
    {
        int n = column - Column;
        if (n > 0) Add(new string(' ', n), Palette.Dim);
        return this;
    }

    /// <summary>Total length of runs.</summary>
    public static int Len(Run[] runs) => runs.Sum(r => r.Text.Length);

    /// <summary>Place runs so that they end at the right edge.</summary>
    public LineBuilder Right(params Run[] runs)
    {
        PadTo(Width - Len(runs));
        foreach (var r in runs) Add(r.Text, r.Color, r.Style, r.Back);
        return this;
    }

    public Line Build()
    {
        // Cut to width (keeps layout stable), then pad.
        int total = 0;
        var runs = _line.Runs;
        for (int i = 0; i < runs.Count; i++)
        {
            int len = runs[i].Text.Length;
            if (total + len > Width)
            {
                runs[i] = runs[i] with { Text = runs[i].Text[..(Width - total)] };
                runs.RemoveRange(i + 1, runs.Count - i - 1);
                total = Width;
                break;
            }
            total += len;
        }
        runs.RemoveAll(r => r.Text.Length == 0);
        PadTo(Width);
        _line.Hits.RemoveAll(h => h.Col >= Width);
        return _line;
    }
}

/// <summary>
/// Column grid of the widget (32 characters): label (5), then 4-character fields at 5, 10 and 15,
/// then a 12-character graph at 20.
/// </summary>
public static class Layout
{
    public const int Width = 32;
    public const int LabelWidth = 5;
    public const int F1 = 5, F2 = 10, F3 = 15;
    public const int FieldWidth = 4;
    public const int GraphCol = 20;
    public const int GraphChars = 12;   // 24 samples

    public static Line Blank(bool padding = false) => new LineBuilder(Width, padding).Build();

    /// <summary>
    /// Section header in the theme's header style, aligned left, center or right. On the left (right),
    /// <see cref="Theme.HeaderIndent"/> rule characters come before (after) the title.
    /// </summary>
    public static Line Header(string name, ThemeColors c)
    {
        var t = c.Theme;
        var hs = t.HeaderBold ? RunStyle.Bold : RunStyle.None;
        string title = t.HeaderUpper ? name.ToUpperInvariant() : name.ToLowerInvariant();
        if (t.HeaderStyle == "spaced") title = string.Join(' ', title.ToCharArray());
        // Decoration around the title (rule colour) and the fill on both sides.
        var (open, close) = t.HeaderStyle switch
        {
            "bracket" => ("┤ ", " ├"),
            "plain" => ("", ""),
            _ => (" ", " "),
        };
        if (title.Length > Width - 8) title = title[..(Width - 8)];
        // Optional section icon before the title: icon (2 cells) and a space.
        string iconCells = t.HeaderIcons ? Icons.ForSection(name) + "  " : "";
        string align = t.HeaderAlign is "center" or "right" ? t.HeaderAlign : "left";
        int indent = Math.Max(0, t.HeaderIndent);
        // At the edge: no space between the edge and the title.
        if (indent == 0 && align == "left" && open == " ") open = "";
        if (indent == 0 && align == "right" && close == " ") close = "";
        int len = open.Length + iconCells.Length + title.Length + close.Length;
        indent = Math.Min(indent, Width - len);
        int lead = align switch
        {
            "center" => (Width - len) / 2,
            "right" => Width - len - indent,
            _ => indent,
        };
        int trail = Width - lead - len;

        var b = new LineBuilder();
        Fill(b, t.HeaderStyle, lead, left: true, c);
        b.Add(open, c.Rule);
        if (iconCells.Length > 0) b.Icon(iconCells[0], c.Header).Add(" ", c.Rule);
        b.Add(title, c.Header, hs).Add(close, c.Rule);
        Fill(b, t.HeaderStyle, trail, left: false, c);
        return b.Build();
    }

    /// <summary>Header fill of <paramref name="n"/> cells in the style's rule characters.</summary>
    private static void Fill(LineBuilder b, string style, int n, bool left, ThemeColors c)
    {
        if (n <= 0) return;
        switch (style)
        {
            case "plain":
                b.Add(new string(' ', n), c.Rule);
                break;
            case "dots":
                for (int i = 0; i < n; i++) b.Add(b.Column % 2 == 0 ? "·" : " ", c.Rule);
                break;
            case "fade":
            {
                // Strong next to the title, fading out to the edge.
                int third = n / 3, rest = n - 2 * third;
                if (left) b.Add(new string('╌', rest), c.Dim).Add(new string('─', third), c.Rule).Add(new string('━', third), c.Rule);
                else b.Add(new string('━', third), c.Rule).Add(new string('─', third), c.Rule).Add(new string('╌', rest), c.Dim);
                break;
            }
            default:
                b.Add(new string('─', n), c.Rule);
                break;
        }
    }

    public static readonly string[] SpacerPresets = { "blank", "rule", "dots", "dashed", "double", "custom" };

    /// <summary>Spacer line: blank, rule, dots, dashed, double, or custom text.</summary>
    public static Line Spacer(string? preset, string? text, ThemeColors c)
    {
        var b = new LineBuilder();
        switch (preset)
        {
            case "rule": b.Add(new string('─', Width), c.Rule); break;
            case "dots": for (int i = 0; i < Width; i++) b.Add(i % 2 == 0 ? "·" : " ", c.Rule); break;
            case "dashed": b.Add(new string('╌', Width), c.Rule); break;
            case "double": b.Add(new string('═', Width), c.Rule); break;
            case "custom": b.Add(text ?? "", c.Unit); break;
            default: break;
        }
        return b.Build();
    }
}
