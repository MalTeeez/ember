using System.Text.RegularExpressions;

namespace Ember.Core.Notes;

[Flags]
public enum MdStyle
{
    Normal = 0,
    Marker = 1,     // dim markdown markers
    Bold = 2,
    Italic = 4,
    Code = 8,
    Heading = 16,
    Done = 32,      // ticked checkbox text (dim, strike-out)
}

public readonly record struct MdSpan(int Start, int Length, MdStyle Style)
{
    public int End => Start + Length;
}

/// <summary>
/// Simple markdown styling for the note editor: headings, bold, italic, inline code,
/// lists and checkboxes. Markers stay in the text; they get the Marker style (dim).
/// Lines are separated by '\r' or '\n' (one character each, as in RichEdit; "\r\n" is two).
/// Spans may overlap; the editor applies them in order.
/// </summary>
public static partial class MarkdownStyler
{
    [GeneratedRegex(@"^(\s*)([-*+]|\d+[.)])\s")]
    private static partial Regex ListRegex();

    [GeneratedRegex(@"^(\s*)([-*+])\s\[([ xX])\]\s?")]
    private static partial Regex CheckboxRegex();

    [GeneratedRegex(@"^(#{1,6})\s")]
    private static partial Regex HeadingRegex();

    public static List<MdSpan> Style(string text)
    {
        var spans = new List<MdSpan>();
        int pos = 0;
        while (pos <= text.Length)
        {
            int end = text.IndexOfAny(new[] { '\r', '\n' }, pos);
            if (end < 0) end = text.Length;
            StyleLine(text.AsSpan(pos, end - pos).ToString(), pos, spans);
            if (end >= text.Length) break;
            pos = end + 1;
        }
        return spans;
    }

    private static void StyleLine(string line, int offset, List<MdSpan> spans)
    {
        if (line.Length == 0) return;

        var h = HeadingRegex().Match(line);
        if (h.Success)
        {
            spans.Add(new MdSpan(offset, line.Length, MdStyle.Heading | MdStyle.Bold));
            spans.Add(new MdSpan(offset, h.Groups[1].Length, MdStyle.Marker | MdStyle.Heading | MdStyle.Bold));
            StyleInline(line, h.Length, offset, spans, MdStyle.Heading | MdStyle.Bold);
            return;
        }

        int bodyStart = 0;
        MdStyle baseStyle = MdStyle.Normal;
        var cb = CheckboxRegex().Match(line);
        if (cb.Success)
        {
            bool done = cb.Groups[3].Value is "x" or "X";
            spans.Add(new MdSpan(offset + cb.Groups[2].Index, cb.Length - cb.Groups[2].Index, MdStyle.Marker));
            bodyStart = cb.Length;
            if (done)
            {
                baseStyle = MdStyle.Done;
                if (line.Length > bodyStart) spans.Add(new MdSpan(offset + bodyStart, line.Length - bodyStart, MdStyle.Done));
            }
        }
        else
        {
            var li = ListRegex().Match(line);
            if (li.Success)
            {
                spans.Add(new MdSpan(offset + li.Groups[2].Index, li.Groups[2].Length, MdStyle.Marker));
                bodyStart = li.Length;
            }
        }
        StyleInline(line, bodyStart, offset, spans, baseStyle);
    }

    /// <summary>Inline code first (no styling inside it), then bold (**,__), then italic (*,_).</summary>
    private static void StyleInline(string line, int start, int offset, List<MdSpan> spans, MdStyle baseStyle)
    {
        var used = new bool[line.Length];

        // Inline code: `code`
        int i = start;
        while (i < line.Length)
        {
            int a = line.IndexOf('`', i);
            if (a < 0) break;
            int b = line.IndexOf('`', a + 1);
            if (b < 0) break;
            spans.Add(new MdSpan(offset + a, b - a + 1, baseStyle | MdStyle.Code));
            spans.Add(new MdSpan(offset + a, 1, baseStyle | MdStyle.Code | MdStyle.Marker));
            spans.Add(new MdSpan(offset + b, 1, baseStyle | MdStyle.Code | MdStyle.Marker));
            for (int k = a; k <= b; k++) used[k] = true;
            i = b + 1;
        }

        Delimited(line, start, offset, spans, used, "**", baseStyle | MdStyle.Bold);
        Delimited(line, start, offset, spans, used, "__", baseStyle | MdStyle.Bold);
        Delimited(line, start, offset, spans, used, "*", baseStyle | MdStyle.Italic);
        Delimited(line, start, offset, spans, used, "_", baseStyle | MdStyle.Italic);
    }

    private static void Delimited(string line, int start, int offset, List<MdSpan> spans, bool[] used, string d, MdStyle style)
    {
        int i = start;
        while (i < line.Length)
        {
            int a = IndexFree(line, d, i, used);
            if (a < 0) return;
            int contentStart = a + d.Length;
            // Opening marker must be followed by a non-space character.
            if (contentStart >= line.Length || char.IsWhiteSpace(line[contentStart])) { i = a + d.Length; continue; }
            // Single '_' inside a word (snake_case) is not italic.
            if (d == "_" && a > 0 && char.IsLetterOrDigit(line[a - 1])) { i = a + 1; continue; }
            // Closing marker: not directly after the opening one, and not after a space.
            int b = IndexFree(line, d, contentStart, used);
            while (b >= 0 && (b == contentStart || char.IsWhiteSpace(line[b - 1])))
                b = IndexFree(line, d, b + 1, used);
            if (b < 0) return;
            int end = b + d.Length;
            spans.Add(new MdSpan(offset + a, end - a, style));
            spans.Add(new MdSpan(offset + a, d.Length, style | MdStyle.Marker));
            spans.Add(new MdSpan(offset + b, d.Length, style | MdStyle.Marker));
            for (int k = a; k < a + d.Length; k++) used[k] = true;
            for (int k = b; k < end; k++) used[k] = true;
            i = end;
        }
    }

    private static int IndexFree(string line, string d, int from, bool[] used)
    {
        while (from < line.Length)
        {
            int a = line.IndexOf(d, from, StringComparison.Ordinal);
            if (a < 0) return -1;
            bool free = true;
            for (int k = a; k < a + d.Length; k++) if (used[k]) { free = false; break; }
            // A single '*' that is part of '**' is not a single marker.
            if (free && d.Length == 1)
            {
                bool doubled = (a + 1 < line.Length && line[a + 1] == d[0] && !used[a + 1]) ||
                               (a > 0 && line[a - 1] == d[0] && !used[a - 1]);
                if (doubled) { from = a + 2; continue; }
            }
            if (free) return a;
            from = a + 1;
        }
        return -1;
    }

    /// <summary>
    /// If <paramref name="index"/> is on the "[ ]" / "[x]" of a checkbox line, returns the index
    /// of the character between the brackets; otherwise -1.
    /// </summary>
    public static int CheckboxAt(string text, int index)
    {
        if (index < 0 || index > text.Length) return -1;
        int lineStart = index == 0 ? 0 : text.LastIndexOfAny(new[] { '\r', '\n' }, Math.Max(0, index - 1)) + 1;
        if (index > 0 && (text[index - 1] == '\r' || text[index - 1] == '\n')) lineStart = index;
        int lineEnd = text.IndexOfAny(new[] { '\r', '\n' }, lineStart);
        if (lineEnd < 0) lineEnd = text.Length;
        string line = text[lineStart..lineEnd];
        var m = CheckboxRegex().Match(line);
        if (!m.Success) return -1;
        int open = lineStart + m.Groups[3].Index - 1; // '['
        if (index >= open && index <= open + 2) return open + 1;
        return -1;
    }

    /// <summary>Toggles the checkbox mark at <paramref name="markIndex"/> (from <see cref="CheckboxAt"/>).</summary>
    public static string Toggle(string text, int markIndex)
    {
        char c = text[markIndex];
        char n = c is 'x' or 'X' ? ' ' : 'x';
        return string.Concat(text.AsSpan(0, markIndex), n.ToString(), text.AsSpan(markIndex + 1));
    }
}
