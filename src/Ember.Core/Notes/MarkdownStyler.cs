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
    Strike = 64,    // ~~text~~
    Link = 128,     // [text](url) text, or a bare URL
    Quote = 256,    // > quote (Level = depth), whole line
    CodeBlock = 512,// lines inside ``` or ~~~ fences (and the fences)
    Rule = 1024,    // --- / *** / ___ line
}

/// <summary>A styled range. Level: heading level (1..6) or quote depth.</summary>
public readonly record struct MdSpan(int Start, int Length, MdStyle Style, int Level = 0)
{
    public int End => Start + Length;
}

/// <summary>
/// Simple markdown styling for the note editor: headings (levels), bold, italic, strike-through,
/// inline code, fenced code blocks, quotes (nested), links, horizontal rules, lists and checkboxes. Markers stay in the text; they get the Marker style (dim).
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

    [GeneratedRegex(@"^\s{0,3}(```|~~~)")]
    private static partial Regex FenceRegex();

    [GeneratedRegex(@"^(\s*>\s?)+")]
    private static partial Regex QuoteRegex();

    [GeneratedRegex(@"^\s{0,3}([-*_])(\s*\1){2,}\s*$")]
    private static partial Regex RuleRegex();

    [GeneratedRegex(@"\[([^\]\r\n]+)\]\(([^)\s]+)\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"https?://[^\s<>()\[\]]+[^\s<>()\[\].,;:!?'""]")]
    private static partial Regex UrlRegex();

    public static List<MdSpan> Style(string text)
    {
        var spans = new List<MdSpan>();
        int pos = 0;
        string? fence = null;
        while (pos <= text.Length)
        {
            int end = text.IndexOfAny(new[] { '\r', '\n' }, pos);
            if (end < 0) end = text.Length;
            string line = text.AsSpan(pos, end - pos).ToString();
            var f = FenceRegex().Match(line);
            if (fence == null && f.Success)
            {
                fence = f.Groups[1].Value;
                spans.Add(new MdSpan(pos, line.Length, MdStyle.CodeBlock | MdStyle.Marker));
            }
            else if (fence != null)
            {
                bool closing = f.Success && f.Groups[1].Value == fence;
                spans.Add(new MdSpan(pos, line.Length, closing ? MdStyle.CodeBlock | MdStyle.Marker : MdStyle.CodeBlock));
                if (closing) fence = null;
            }
            else StyleLine(line, pos, spans);
            if (end >= text.Length) break;
            pos = end + 1;
        }
        return spans;
    }

    private static void StyleLine(string line, int offset, List<MdSpan> spans)
    {
        if (line.Length == 0) return;

        if (RuleRegex().IsMatch(line))
        {
            spans.Add(new MdSpan(offset, line.Length, MdStyle.Rule | MdStyle.Marker));
            return;
        }

        // Quote prefix: "> ", ">> ", "> > " (depth = number of '>').
        var q = QuoteRegex().Match(line);
        if (q.Success && q.Value.Contains('>'))
        {
            int depth = q.Value.Count(ch => ch == '>');
            spans.Add(new MdSpan(offset, line.Length, MdStyle.Quote, depth));
            spans.Add(new MdSpan(offset, q.Length, MdStyle.Quote | MdStyle.Marker, depth));
            StyleLine(line[q.Length..], offset + q.Length, spans);
            return;
        }

        var h = HeadingRegex().Match(line);
        if (h.Success)
        {
            int level = h.Groups[1].Length;
            spans.Add(new MdSpan(offset, line.Length, MdStyle.Heading | MdStyle.Bold, level));
            spans.Add(new MdSpan(offset, h.Groups[1].Length, MdStyle.Marker | MdStyle.Heading | MdStyle.Bold, level));
            StyleInline(line, h.Length, offset, spans, MdStyle.Heading | MdStyle.Bold, level);
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

    /// <summary>Inline code first (no styling inside it), then links, then bold (**,__), strike (~~), italic (*,_).</summary>
    private static void StyleInline(string line, int start, int offset, List<MdSpan> spans, MdStyle baseStyle, int level = 0)
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
            spans.Add(new MdSpan(offset + a, b - a + 1, baseStyle | MdStyle.Code, level));
            spans.Add(new MdSpan(offset + a, 1, baseStyle | MdStyle.Code | MdStyle.Marker, level));
            spans.Add(new MdSpan(offset + b, 1, baseStyle | MdStyle.Code | MdStyle.Marker, level));
            for (int k = a; k <= b; k++) used[k] = true;
            i = b + 1;
        }

        // Links: [text](url) → text styled as link, brackets and (url) dim.
        foreach (Match m in LinkRegex().Matches(line, start))
        {
            if (Enumerable.Range(m.Index, m.Length).Any(k => used[k])) continue;
            var text = m.Groups[1];
            spans.Add(new MdSpan(offset + m.Index, 1, baseStyle | MdStyle.Marker, level));
            spans.Add(new MdSpan(offset + text.Index, text.Length, baseStyle | MdStyle.Link, level));
            int rest = text.Index + text.Length;
            spans.Add(new MdSpan(offset + rest, m.Index + m.Length - rest, baseStyle | MdStyle.Marker, level));
            for (int k = m.Index; k < m.Index + m.Length; k++) used[k] = true;
        }
        // Bare URLs.
        foreach (Match m in UrlRegex().Matches(line, start))
        {
            if (Enumerable.Range(m.Index, m.Length).Any(k => used[k])) continue;
            spans.Add(new MdSpan(offset + m.Index, m.Length, baseStyle | MdStyle.Link, level));
            for (int k = m.Index; k < m.Index + m.Length; k++) used[k] = true;
        }

        Delimited(line, start, offset, spans, used, "**", baseStyle | MdStyle.Bold, level);
        Delimited(line, start, offset, spans, used, "__", baseStyle | MdStyle.Bold, level);
        Delimited(line, start, offset, spans, used, "~~", baseStyle | MdStyle.Strike, level);
        Delimited(line, start, offset, spans, used, "*", baseStyle | MdStyle.Italic, level);
        Delimited(line, start, offset, spans, used, "_", baseStyle | MdStyle.Italic, level);
    }

    private static void Delimited(string line, int start, int offset, List<MdSpan> spans, bool[] used, string d, MdStyle style, int level)
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
            spans.Add(new MdSpan(offset + a, end - a, style, level));
            spans.Add(new MdSpan(offset + a, d.Length, style | MdStyle.Marker, level));
            spans.Add(new MdSpan(offset + b, d.Length, style | MdStyle.Marker, level));
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

    /// <summary>The URL of a link or bare URL at <paramref name="index"/>, or null.</summary>
    public static string? LinkAt(string text, int index)
    {
        if (index < 0 || index > text.Length) return null;
        int lineStart = index == 0 ? 0 : text.LastIndexOfAny(new[] { '\r', '\n' }, Math.Max(0, index - 1)) + 1;
        if (index > 0 && (text[index - 1] == '\r' || text[index - 1] == '\n')) lineStart = index;
        int lineEnd = text.IndexOfAny(new[] { '\r', '\n' }, lineStart);
        if (lineEnd < 0) lineEnd = text.Length;
        string line = text[lineStart..lineEnd];
        int i = index - lineStart;
        foreach (Match m in LinkRegex().Matches(line))
            if (i >= m.Index && i < m.Index + m.Length) return m.Groups[2].Value;
        foreach (Match m in UrlRegex().Matches(line))
            if (i >= m.Index && i <= m.Index + m.Length) return m.Value;
        return null;
    }

    /// <summary>Toggles the checkbox mark at <paramref name="markIndex"/> (from <see cref="CheckboxAt"/>).</summary>
    public static string Toggle(string text, int markIndex)
    {
        char c = text[markIndex];
        char n = c is 'x' or 'X' ? ' ' : 'x';
        return string.Concat(text.AsSpan(0, markIndex), n.ToString(), text.AsSpan(markIndex + 1));
    }
}
