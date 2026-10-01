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

/// <summary>A piece of text with one colour and style.</summary>
public readonly record struct Run(string Text, Rgb Color, RunStyle Style = RunStyle.None);

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

    public static bool IsIcon(char c) => c >= '' && c <= '';
}

/// <summary>One widget line: styled runs plus clickable ranges.</summary>
public sealed class Line
{
    public List<Run> Runs { get; } = new();
    public List<Hit> Hits { get; } = new();
    /// <summary>A padding line that keeps space (layout stabilizer). Drawn empty.</summary>
    public bool IsPadding { get; init; }

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
            foreach (var r in Runs) sb.Append(r.Color.ToHex()).Append((int)r.Style).Append('|').Append(r.Text).Append('\u0001');
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

    public LineBuilder Add(string text, Rgb color, RunStyle style = RunStyle.None)
    {
        if (text.Length == 0) return this;
        var runs = _line.Runs;
        if (runs.Count > 0 && runs[^1].Color == color && runs[^1].Style == style && (style & RunStyle.Icon) == 0)
            runs[^1] = new Run(runs[^1].Text + text, color, style);
        else
            runs.Add(new Run(text, color, style));
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
        foreach (var r in runs) Add(r.Text, r.Color, r.Style);
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

    /// <summary>Section header in the theme's header style.</summary>
    public static Line Header(string name, ThemeColors c)
    {
        var t = c.Theme;
        bool bold = t.HeaderBold;
        var hs = bold ? RunStyle.Bold : RunStyle.None;
        string title = t.HeaderUpper ? name.ToUpperInvariant() : name.ToLowerInvariant();
        var b = new LineBuilder();
        switch (t.HeaderStyle)
        {
            case "spaced":
                b.Add("─ ", c.Rule).Add(string.Join(' ', title.ToCharArray()), c.Header, hs).Add(" ", c.Rule);
                Fill(b, '─', c.Rule);
                break;
            case "plain":
                b.Add(title, c.Header, hs);
                break;
            case "bracket":
                b.Add("──┤ ", c.Rule).Add(title, c.Header, hs).Add(" ├", c.Rule);
                Fill(b, '─', c.Rule);
                break;
            case "dots":
                b.Add(title, c.Header, hs).Add(" ", c.Rule);
                while (b.Column < Width) b.Add(b.Column % 2 == 0 ? "·" : " ", c.Rule);
                break;
            case "fade":
            {
                b.Add(title, c.Header, hs).Add(" ", c.Rule);
                int rest = Width - b.Column, third = rest / 3;
                b.Add(new string('━', third), c.Rule).Add(new string('─', third), c.Rule).Add(new string('╌', rest - 2 * third), c.Dim);
                break;
            }
            default: // rule
                b.Add("── ", c.Rule).Add(title, c.Header, hs).Add(" ", c.Rule);
                Fill(b, '─', c.Rule);
                break;
        }
        return b.Build();
    }

    private static void Fill(LineBuilder b, char ch, Rgb color)
    {
        if (b.Column < Width) b.Add(new string(ch, Width - b.Column), color);
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
