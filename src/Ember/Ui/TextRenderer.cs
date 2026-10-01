using Ember.App;
using Ember.Core.Render;
using Ember.Native;

namespace Ember.Ui;

/// <summary>
/// Draws widget lines with GDI+ on a fixed character grid. Bold runs use a bold font; icon runs use
/// the icon font (Segoe Fluent Icons, else Segoe MDL2 Assets) centred in their 2 cells. Characters
/// that the main font does not have (for example braille in Consolas) are drawn with a fallback font,
/// centred in their cell (so they sit on the same middle line as the text).
/// </summary>
internal sealed class TextRenderer : IDisposable
{
    private const string FallbackFace = "Segoe UI Symbol";
    private const string SpecialChars = "⣿⡀█▉▊▋▌▍▎▏│└─━╌═┤├↓↑°…—■•░";

    private IntPtr _family, _font, _bold, _fbFamily, _fbFont, _iconFamily, _iconFont, _format, _center, _brush;
    private readonly HashSet<char> _missing = new();
    private bool _missingBraille;
    public float CellWidth { get; private set; } = 8;
    public float LineHeight { get; private set; } = 16;
    public float EmPixels { get; private set; } = 13;
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
        EmPixels = px;
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
        if (Gdip.GdipCreateFont(_family, px, Gdip.FontStyleBold, Gdip.UnitPixel, out _bold) != 0) _bold = IntPtr.Zero;
        if (Gdip.GdipCreateFontFamilyFromName(FallbackFace, IntPtr.Zero, out _fbFamily) == 0)
            Gdip.GdipCreateFont(_fbFamily, px, Gdip.FontStyleRegular, Gdip.UnitPixel, out _fbFont);
        if (Gdip.GdipCreateFontFamilyFromName("Segoe Fluent Icons", IntPtr.Zero, out _iconFamily) != 0 &&
            Gdip.GdipCreateFontFamilyFromName("Segoe MDL2 Assets", IntPtr.Zero, out _iconFamily) != 0)
            _iconFamily = IntPtr.Zero;
        if (_iconFamily != IntPtr.Zero)
            Gdip.GdipCreateFont(_iconFamily, px * 0.9f, Gdip.FontStyleRegular, Gdip.UnitPixel, out _iconFont);

        Gdip.GdipStringFormatGetGenericTypographic(out var generic);
        Gdip.GdipCloneStringFormat(generic, out _format);
        Gdip.GdipSetStringFormatFlags(_format, Gdip.StringFormatFlagsMeasureTrailingSpaces | Gdip.StringFormatFlagsNoWrap | Gdip.StringFormatFlagsNoClip);
        Gdip.GdipCloneStringFormat(generic, out _center);
        Gdip.GdipSetStringFormatFlags(_center, Gdip.StringFormatFlagsNoWrap | Gdip.StringFormatFlagsNoClip);
        Gdip.GdipSetStringFormatAlign(_center, Gdip.StringAlignmentCenter);
        Gdip.GdipSetStringFormatLineAlign(_center, Gdip.StringAlignmentCenter);
        if (_brush == IntPtr.Zero) Gdip.GdipCreateSolidFill(0xFFFFFFFF, out _brush);

        FindMissingGlyphs(used, (int)Math.Round(px));
        Measure();
        Log.Write($"font {used} {sizePt}pt dpi {dpi}: cell {CellWidth:0.00}x{LineHeight:0.00}, icons {(_iconFont != IntPtr.Zero)}, missing '{new string(_missing.ToArray())}'");
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

    /// <summary>
    /// Draws the lines. Text alpha = <paramref name="alpha"/> (text opacity). With <paramref name="shadow"/>,
    /// each glyph first gets a dark copy 1 px down-right. Lines below <paramref name="maxY"/> are not drawn.
    /// </summary>
    public void Draw(IntPtr g, IReadOnlyList<Line> lines, float x0, float y0, byte alpha, bool shadow, float maxY = float.MaxValue)
    {
        Gdip.GdipSetTextRenderingHint(g, Gdip.TextRenderingHintAntiAliasGridFit);
        if (shadow)
        {
            float off = Math.Max(1, EmPixels / 13f);
            DrawPass(g, lines, x0 + off, y0 + off, _ => (uint)(Math.Min(alpha, (byte)170) << 24), maxY);
        }
        DrawPass(g, lines, x0, y0, c => c.ToArgb(alpha), maxY);
    }

    private void DrawPass(IntPtr g, IReadOnlyList<Line> lines, float x0, float y0, Func<Rgb, uint> color, float maxY)
    {
        float y = y0;
        var seg = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            if (y >= maxY) break;
            if (line.IsPadding) { y += LineHeight; continue; }
            int col = 0;
            foreach (var run in line.Runs)
            {
                Gdip.GdipSetSolidFillColor(_brush, color(run.Color));
                if ((run.Style & RunStyle.Icon) != 0)
                {
                    if (run.Text.Length > 0 && _iconFont != IntPtr.Zero)
                        DrawIcon(g, run.Text[0].ToString(), color(run.Color), x0 + col * CellWidth, y, CellWidth * 2);
                    col += run.Text.Length;
                    continue;
                }
                var font = (run.Style & RunStyle.Bold) != 0 && _bold != IntPtr.Zero ? _bold : _font;
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
                        Flush(g, seg, font, x0, segStart, y);
                        if (c != '⠀') // blank braille: nothing to draw
                        {
                            if (UseFallback(c)) DrawCentered(g, c.ToString(), _fbFont, x0 + col * CellWidth, y, CellWidth);
                            else DrawText(g, c.ToString(), font, x0 + col * CellWidth, y);
                        }
                    }
                    col++;
                }
                Flush(g, seg, font, x0, segStart, y);
            }
            y += LineHeight;
        }
    }

    private void Flush(IntPtr g, System.Text.StringBuilder seg, IntPtr font, float x0, int col, float y)
    {
        if (seg.Length == 0) return;
        string s = seg.ToString();
        seg.Clear();
        if (s.Trim().Length == 0) return;
        DrawText(g, s, font, x0 + col * CellWidth, y);
    }

    private void DrawText(IntPtr g, string s, IntPtr font, float x, float y)
    {
        var rect = new RectF(x, y, 10000, LineHeight * 2);
        Gdip.GdipDrawString(g, s, s.Length, font, ref rect, _format, _brush);
    }

    /// <summary>Centred in a cell box of the line height (icons, fallback glyphs).</summary>
    /// <summary>
    /// Icon glyph as a path, filled and stroked, so its lines are about as thick as the text
    /// (the icon fonts have hairline strokes at small sizes).
    /// </summary>
    private void DrawIcon(IntPtr g, string s, uint argb, float x, float y, float width)
    {
        var rect = new RectF(x, y, width, LineHeight);
        if (!Gdip.DrawGlyphPath(g, _brush, argb, _iconFamily, EmPixels * 0.9f, rect, _center, s))
            DrawCentered(g, s, _iconFont, x, y, width);
    }

    private void DrawCentered(IntPtr g, string s, IntPtr font, float x, float y, float width)
    {
        var rect = new RectF(x, y, width, LineHeight);
        Gdip.GdipDrawString(g, s, s.Length, font, ref rect, _center, _brush);
    }

    private void FreeFonts()
    {
        foreach (var f in new[] { _font, _bold, _fbFont, _iconFont })
            if (f != IntPtr.Zero) Gdip.GdipDeleteFont(f);
        _font = _bold = _fbFont = _iconFont = IntPtr.Zero;
        if (_family != IntPtr.Zero) Gdip.GdipDeleteFontFamily(_family);
        if (_fbFamily != IntPtr.Zero) Gdip.GdipDeleteFontFamily(_fbFamily);
        if (_iconFamily != IntPtr.Zero) Gdip.GdipDeleteFontFamily(_iconFamily);
        _family = _fbFamily = _iconFamily = IntPtr.Zero;
        if (_format != IntPtr.Zero) { Gdip.GdipDeleteStringFormat(_format); _format = IntPtr.Zero; }
        if (_center != IntPtr.Zero) { Gdip.GdipDeleteStringFormat(_center); _center = IntPtr.Zero; }
        _key = "";
    }

    public void Dispose()
    {
        FreeFonts();
        if (_brush != IntPtr.Zero) { Gdip.GdipDeleteBrush(_brush); _brush = IntPtr.Zero; }
    }
}
