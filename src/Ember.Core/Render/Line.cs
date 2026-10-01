using System.Text;

namespace Ember.Core.Render;

/// <summary>A piece of text with one colour.</summary>
public readonly record struct Run(string Text, Rgb Color);

/// <summary>What a click on a line acts on.</summary>
public enum ClickKind { None, Process }

/// <summary>One widget line: coloured runs plus an optional click target.</summary>
public sealed class Line
{
    public List<Run> Runs { get; } = new();
    public ClickKind Click { get; init; }
    public int Pid { get; init; }

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

    /// <summary>Text and colours as one comparable key (used to redraw only on change).</summary>
    public string Key
    {
        get
        {
            var sb = new StringBuilder();
            foreach (var r in Runs) sb.Append(r.Color.ToString()).Append('|').Append(r.Text).Append('\u0001');
            sb.Append((int)Click).Append(':').Append(Pid);
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

    public LineBuilder(int width = Layout.Width, ClickKind click = ClickKind.None, int pid = 0)
    {
        Width = width;
        _line = new Line { Click = click, Pid = pid };
    }

    public int Column => _line.Length;

    public LineBuilder Add(string text, Rgb color)
    {
        if (text.Length == 0) return this;
        var runs = _line.Runs;
        if (runs.Count > 0 && runs[^1].Color == color)
            runs[^1] = new Run(runs[^1].Text + text, color);
        else
            runs.Add(new Run(text, color));
        return this;
    }

    public LineBuilder Dim(string text) => Add(text, Palette.Dim);
    public LineBuilder Bright(string text) => Add(text, Palette.Bright);

    /// <summary>Pad with spaces to the column.</summary>
    public LineBuilder PadTo(int column)
    {
        int n = column - Column;
        if (n > 0) Add(new string(' ', n), Palette.Dim);
        return this;
    }

    /// <summary>Place runs so that they end at the right edge.</summary>
    public LineBuilder Right(params Run[] runs)
    {
        int len = 0;
        foreach (var r in runs) len += r.Text.Length;
        PadTo(Width - len);
        foreach (var r in runs) Add(r.Text, r.Color);
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
                runs[i] = new Run(runs[i].Text[..(Width - total)], runs[i].Color);
                runs.RemoveRange(i + 1, runs.Count - i - 1);
                total = Width;
                break;
            }
            total += len;
        }
        runs.RemoveAll(r => r.Text.Length == 0);
        PadTo(Width);
        return _line;
    }
}

/// <summary>Column grid of the widget (32 characters).</summary>
public static class Layout
{
    public const int Width = 32;
    public const int LabelCol = 0;      // 4 chars
    public const int ValueCol = 4;      // 5 chars
    public const int FieldCol = 9;      // 5 chars (unit or temperature)
    public const int GraphCol = 14;     // 12 chars
    public const int GraphChars = 12;   // 24 samples
    public const int PeakCol = 26;      // 6 chars

    /// <summary>Section header: "─ G P U ─────…" (dim).</summary>
    public static Line Header(string name)
    {
        string spaced = string.Join(' ', name.ToUpperInvariant().ToCharArray());
        string text = "─ " + spaced + " ";
        if (text.Length < Width) text += new string('─', Width - text.Length);
        return new LineBuilder().Dim(text).Build();
    }

    public static Line Blank() => new LineBuilder().Build();
}
