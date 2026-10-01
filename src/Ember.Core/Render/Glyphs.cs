namespace Ember.Core.Render;

/// <summary>Braille graphs and 1/8 block bars.</summary>
public static class Glyphs
{
    public const char BrailleBlank = '⠀';

    // Dots from bottom to top. Left column: dots 7,3,2,1. Right column: dots 8,6,5,4.
    private static readonly int[] LeftBits = { 0x40, 0x04, 0x02, 0x01 };
    private static readonly int[] RightBits = { 0x80, 0x20, 0x10, 0x08 };

    private const string Partials = "▏▎▍▌▋▊▉"; // ▏▎▍▌▋▊▉
    public const char Full = '█'; // █

    /// <summary>Level 1..4 for one sample. 0 (or below) gives the baseline dot (level 1).</summary>
    public static int Level(double value, double scale)
    {
        if (double.IsNaN(value) || scale <= 0 || value <= 0) return 1;
        double r = Math.Min(value / scale, 1.0);
        int level = 1 + (int)Math.Round(3 * r, MidpointRounding.AwayFromZero);
        return Math.Clamp(level, 1, 4);
    }

    /// <summary>Braille character for two samples (left, right). Level 0 = no dots.</summary>
    public static char BrailleChar(int leftLevel, int rightLevel)
    {
        int bits = 0;
        for (int i = 0; i < Math.Clamp(leftLevel, 0, 4); i++) bits |= LeftBits[i];
        for (int i = 0; i < Math.Clamp(rightLevel, 0, 4); i++) bits |= RightBits[i];
        return (char)(0x2800 + bits);
    }

    /// <summary>
    /// A braille graph of <paramref name="chars"/> characters (2 samples each, newest at the right).
    /// Missing history at the left is blank. Also returns, for each character, the largest of
    /// its samples (NaN if none), so the caller can colour each character.
    /// </summary>
    public static (string Text, double[] CellMax) Graph(IReadOnlyList<double> samples, int chars, double scale)
    {
        int needed = chars * 2;
        var text = new char[chars];
        var max = new double[chars];
        int offset = samples.Count - needed; // index in samples of slot 0
        for (int c = 0; c < chars; c++)
        {
            int li = offset + c * 2, ri = li + 1;
            bool hasL = li >= 0 && li < samples.Count;
            bool hasR = ri >= 0 && ri < samples.Count;
            int l = hasL ? Level(samples[li], scale) : 0;
            int r = hasR ? Level(samples[ri], scale) : 0;
            text[c] = BrailleChar(l, r);
            double m = double.NaN;
            if (hasL) m = samples[li];
            if (hasR) m = double.IsNaN(m) ? samples[ri] : Math.Max(m, samples[ri]);
            max[c] = m;
        }
        return (new string(text), max);
    }

    /// <summary>A bar of exactly <paramref name="width"/> characters in 1/8 steps, padded with spaces.</summary>
    public static string Bar(double fraction, int width)
    {
        if (width <= 0) return "";
        if (double.IsNaN(fraction)) fraction = 0;
        fraction = Math.Clamp(fraction, 0, 1);
        int eighths = (int)Math.Round(fraction * width * 8, MidpointRounding.AwayFromZero);
        int full = eighths / 8, rem = eighths % 8;
        var sb = new System.Text.StringBuilder(width);
        sb.Append(Full, full);
        if (rem > 0 && full < width) sb.Append(Partials[rem - 1]);
        while (sb.Length < width) sb.Append(' ');
        return sb.ToString();
    }

    /// <summary>Cell index (0..width-1) of a marker at the given fraction.</summary>
    public static int MarkerCell(double fraction, int width)
    {
        if (double.IsNaN(fraction)) fraction = 0;
        int cell = (int)Math.Floor(Math.Clamp(fraction, 0, 1) * width);
        return Math.Clamp(cell, 0, width - 1);
    }

    /// <summary>
    /// Usage bar in a style. Returns the text and the number of filled cells (the caller colours
    /// filled and empty cells differently). "blocks" uses 1/8 steps; the other styles use one glyph
    /// for filled and one for empty cells.
    /// </summary>
    public static (string Text, int Filled) StyledBar(double fraction, int width, string style)
    {
        if (double.IsNaN(fraction)) fraction = 0;
        fraction = Math.Clamp(fraction, 0, 1);
        if (style == "blocks")
        {
            string bar = Bar(fraction, width);
            int filled = bar.Count(ch => ch != ' ');
            // Empty cells show a dim dot so the bar length is visible.
            return (string.Concat(bar.Select(ch => ch == ' ' ? '\u00B7' : ch)), filled);
        }
        var (on, off) = BarGlyphs(style);
        int n = (int)Math.Round(fraction * width, MidpointRounding.AwayFromZero);
        if (fraction > 0 && n == 0) n = 1; // any use shows at least one cell
        return (new string(on, n) + new string(off, width - n), n);
    }

    public static (char On, char Off) BarGlyphs(string style) => style switch
    {
        "squares" => ('\u25A0', '\u25A0'),   // ■ ■
        "pips" => ('\u2022', '\u00B7'),      // • ·
        "braille" => ('\u28FF', '\u28C0'),   // ⣿ ⣀
        "line" => ('\u2501', '\u2500'),      // ━ ─
        "shade" => ('\u2588', '\u2591'),     // █ ░
        _ => (':', ':'),                     // colons
    };
}
