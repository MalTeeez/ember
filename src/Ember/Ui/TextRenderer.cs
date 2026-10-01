using Ember.App;
using Ember.Core.Render;
using Ember.Native;

namespace Ember.Ui;

/// <summary>
/// Draws widget lines with GDI+ on a fixed character grid. Characters that the main font
/// does not have (for example braille in Consolas) are drawn with a fallback font in their cell.
/// </summary>
internal sealed class TextRenderer : IDisposable
{
    private const string FallbackFace = "Segoe UI Symbol";
    private const string SpecialChars = "\u28FF\u2840\u2588\u2589\u258A\u258B\u258C\u258D\u258E\u258F\u2502\u2514\u2500\u2193\u2191\u21BB\u00B0\u2026\u2014";

    private IntPtr _family, _font, _fbFamily, _fbFont, _format, _brush;
    private readonly HashSet<char> _missing = new();
    private bool _missingBraille;
    public float CellWidth { get; private set; } = 8;
    public float LineHeight { get; private set; } = 16;
    private string _key = "";

    public static IntPtr GdipToken;

    public static void Startup()
    {
        var input = new GdiplusStartupInput { GdiplusVersion = 1 };
        Gdip.GdiplusStartup(out GdipToken, ref input, IntPtr.Zero);
    }

    /// <summary>Creates fonts for the face, size (pt) and DPI. Does nothing if unchanged.</summary>
    public void Configure(string face, double sizePt, uint dpi)
    {
        string key = $"{face}|{sizePt}|{dpi}";
        if (key == _key) return;
        FreeFonts();
        _key = key;

        float px = (float)(sizePt * dpi / 72.0);
        string used = face;
        if (Gdip.GdipCreateFontFamilyFromName(face, IntPtr.Zero, out _family) != 0)
        {
            used = "Consolas";
            if (Gdip.GdipCreateFontFamilyFromName(used, IntPtr.Zero, out _family) != 0)
            {
                used = "Courier New";
                Gdip.GdipCreateFontFamilyFromName(used, IntPtr.Zero, out _family);
            }
        }
        Gdip.GdipCreateFont(_family, px, Gdip.FontStyleRegular, Gdip.UnitPixel, out _font);
        if (Gdip.GdipCreateFontFamilyFromName(FallbackFace, IntPtr.Zero, out _fbFamily) == 0)
            Gdip.GdipCreateFont(_fbFamily, px, Gdip.FontStyleRegular, Gdip.UnitPixel, out _fbFont);

        Gdip.GdipStringFormatGetGenericTypographic(out var generic);
        Gdip.GdipCloneStringFormat(generic, out _format);
        Gdip.GdipSetStringFormatFlags(_format, Gdip.StringFormatFlagsMeasureTrailingSpaces | Gdip.StringFormatFlagsNoWrap | Gdip.StringFormatFlagsNoClip);
        if (_brush == IntPtr.Zero) Gdip.GdipCreateSolidFill(0xFFFFFFFF, out _brush);

        FindMissingGlyphs(used, (int)Math.Round(px));
        Measure();
        Log.Write($"font {used} {sizePt}pt dpi {dpi}: cell {CellWidth:0.00}x{LineHeight:0.00}, missing '{new string(_missing.ToArray())}'");
    }

    private void FindMissingGlyphs(string face, int px)
    {
        _missing.Clear();
        _missingBraille = false;
        var hdc = W.CreateCompatibleDC(IntPtr.Zero);
        var hfont = W.CreateFont(-px, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 0, 0, face);
        var old = W.SelectObject(hdc, hfont);
        try
        {
            var idx = new ushort[SpecialChars.Length];
            if (W.GetGlyphIndices(hdc, SpecialChars, SpecialChars.Length, idx, W.GGI_MARK_NONEXISTING_GLYPHS) != 0xFFFFFFFF)
            {
                for (int i = 0; i < idx.Length; i++)
                    if (idx[i] == 0xFFFF) _missing.Add(SpecialChars[i]);
            }
            _missingBraille = _missing.Contains('⣿');
        }
        finally
        {
            W.SelectObject(hdc, old);
            W.DeleteObject(hfont);
            W.DeleteDC(hdc);
        }
    }

    private void Measure()
    {
        Gdip.GdipCreateBitmapFromScan0(1, 1, 0, Gdip.PixelFormat32bppPARGB, IntPtr.Zero, out var bmp);
        Gdip.GdipGetImageGraphicsContext(bmp, out var g);
        try
        {
            const string sample = "0000000000";
            var layout = new RectF(0, 0, 10000, 1000);
            if (Gdip.GdipMeasureString(g, sample, sample.Length, _font, ref layout, _format, out var bounds, out _, out _) == 0 && bounds.Width > 0)
                CellWidth = bounds.Width / sample.Length;
            if (Gdip.GdipGetFontHeight(_font, g, out var h) == 0 && h > 0)
                LineHeight = (float)Math.Ceiling(h);
        }
        finally
        {
            Gdip.GdipDeleteGraphics(g);
            Gdip.GdipDisposeImage(bmp);
        }
    }

    private bool UseFallback(char c)
        => _fbFont != IntPtr.Zero && (_missing.Contains(c) || (_missingBraille && c >= '⠀' && c <= '⣿'));

    /// <summary>Draws the lines. Characters below U+2000 are drawn as runs; others one by one in their cell.</summary>
    public void Draw(IntPtr g, IReadOnlyList<Line> lines, float x0, float y0)
    {
        Gdip.GdipSetTextRenderingHint(g, Gdip.TextRenderingHintAntiAliasGridFit);
        float y = y0;
        var seg = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            int col = 0;
            foreach (var run in line.Runs)
            {
                Gdip.GdipSetSolidFillColor(_brush, run.Color.ToArgb());
                int segStart = col;
                seg.Clear();
                foreach (char c in run.Text)
                {
                    if (c < ' ' && !UseFallback(c))
                    {
                        if (seg.Length == 0) segStart = col;
                        seg.Append(c);
                    }
                    else
                    {
                        Flush(g, seg, x0, segStart, y);
                        if (c != '⠀') // blank braille: nothing to draw
                            DrawText(g, c.ToString(), UseFallback(c) ? _fbFont : _font, x0 + col * CellWidth, y);
                    }
                    col++;
                }
                Flush(g, seg, x0, segStart, y);
            }
            y += LineHeight;
        }
    }

    private void Flush(IntPtr g, System.Text.StringBuilder seg, float x0, int col, float y)
    {
        if (seg.Length == 0) return;
        string s = seg.ToString();
        seg.Clear();
        if (s.Trim().Length == 0) return;
        DrawText(g, s, _font, x0 + col * CellWidth, y);
    }

    private void DrawText(IntPtr g, string s, IntPtr font, float x, float y)
    {
        var rect = new RectF(x, y, 10000, LineHeight * 2);
        Gdip.GdipDrawString(g, s, s.Length, font, ref rect, _format, _brush);
    }

    private void FreeFonts()
    {
        if (_font != IntPtr.Zero) { Gdip.GdipDeleteFont(_font); _font = IntPtr.Zero; }
        if (_fbFont != IntPtr.Zero) { Gdip.GdipDeleteFont(_fbFont); _fbFont = IntPtr.Zero; }
        if (_family != IntPtr.Zero) { Gdip.GdipDeleteFontFamily(_family); _family = IntPtr.Zero; }
        if (_fbFamily != IntPtr.Zero) { Gdip.GdipDeleteFontFamily(_fbFamily); _fbFamily = IntPtr.Zero; }
        if (_format != IntPtr.Zero) { Gdip.GdipDeleteStringFormat(_format); _format = IntPtr.Zero; }
        _key = "";
    }

    public void Dispose()
    {
        FreeFonts();
        if (_brush != IntPtr.Zero) { Gdip.GdipDeleteBrush(_brush); _brush = IntPtr.Zero; }
    }
}
