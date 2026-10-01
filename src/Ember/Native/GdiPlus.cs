using System.Runtime.InteropServices;

namespace Ember.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct RectF
{
    public float X, Y, Width, Height;
    public RectF(float x, float y, float w, float h) { X = x; Y = y; Width = w; Height = h; }
}

[StructLayout(LayoutKind.Sequential)]
internal struct GdiplusStartupInput
{
    public uint GdiplusVersion;
    public IntPtr DebugEventCallback;
    public int SuppressBackgroundThread;
    public int SuppressExternalCodecs;
}

/// <summary>GDI+ flat API (gdiplus.dll). Status 0 = Ok.</summary>
internal static class Gdip
{
    private const string Dll = "gdiplus.dll";
    public const int PixelFormat32bppPARGB = 0x000E200B;
    public const int UnitPixel = 2;
    public const int FontStyleRegular = 0, FontStyleBold = 1;
    public const int TextRenderingHintAntiAliasGridFit = 3, TextRenderingHintAntiAlias = 4;
    public const int SmoothingModeAntiAlias = 4;
    public const int StringFormatFlagsMeasureTrailingSpaces = 0x0800, StringFormatFlagsNoWrap = 0x1000, StringFormatFlagsNoClip = 0x4000;

    [DllImport(Dll)] public static extern int GdiplusStartup(out IntPtr token, ref GdiplusStartupInput input, IntPtr output);
    [DllImport(Dll)] public static extern void GdiplusShutdown(IntPtr token);

    [DllImport(Dll)] public static extern int GdipCreateBitmapFromScan0(int w, int h, int stride, int format, IntPtr scan0, out IntPtr bitmap);
    [DllImport(Dll)] public static extern int GdipGetImageGraphicsContext(IntPtr image, out IntPtr graphics);
    [DllImport(Dll)] public static extern int GdipDisposeImage(IntPtr image);
    [DllImport(Dll)] public static extern int GdipDeleteGraphics(IntPtr graphics);
    [DllImport(Dll)] public static extern int GdipGraphicsClear(IntPtr graphics, uint argb);
    [DllImport(Dll)] public static extern int GdipSetTextRenderingHint(IntPtr graphics, int hint);
    [DllImport(Dll)] public static extern int GdipSetSmoothingMode(IntPtr graphics, int mode);
    [DllImport(Dll)] public static extern int GdipCreateFromHDC(IntPtr hdc, out IntPtr graphics);

    [DllImport(Dll, CharSet = CharSet.Unicode)] public static extern int GdipCreateFontFamilyFromName(string name, IntPtr collection, out IntPtr family);
    [DllImport(Dll)] public static extern int GdipDeleteFontFamily(IntPtr family);
    [DllImport(Dll)] public static extern int GdipCreateFont(IntPtr family, float emSize, int style, int unit, out IntPtr font);
    [DllImport(Dll)] public static extern int GdipDeleteFont(IntPtr font);
    [DllImport(Dll)] public static extern int GdipGetFontHeight(IntPtr font, IntPtr graphics, out float height);

    [DllImport(Dll)] public static extern int GdipCreateSolidFill(uint argb, out IntPtr brush);
    [DllImport(Dll)] public static extern int GdipSetSolidFillColor(IntPtr brush, uint argb);
    [DllImport(Dll)] public static extern int GdipDeleteBrush(IntPtr brush);
    [DllImport(Dll)] public static extern int GdipFillRectangle(IntPtr graphics, IntPtr brush, float x, float y, float w, float h);
    [DllImport(Dll)] public static extern int GdipFillEllipse(IntPtr graphics, IntPtr brush, float x, float y, float w, float h);

    [DllImport(Dll)] public static extern int GdipStringFormatGetGenericTypographic(out IntPtr format);
    [DllImport(Dll)] public static extern int GdipStringFormatGetGenericDefault(out IntPtr format);
    [DllImport(Dll)] public static extern int GdipCloneStringFormat(IntPtr format, out IntPtr clone);
    [DllImport(Dll)] public static extern int GdipSetStringFormatFlags(IntPtr format, int flags);
    [DllImport(Dll)] public static extern int GdipDeleteStringFormat(IntPtr format);

    [DllImport(Dll, CharSet = CharSet.Unicode)]
    public static extern int GdipDrawString(IntPtr graphics, string text, int length, IntPtr font, ref RectF layout, IntPtr format, IntPtr brush);
    [DllImport(Dll, CharSet = CharSet.Unicode)]
    public static extern int GdipMeasureString(IntPtr graphics, string text, int length, IntPtr font, ref RectF layout, IntPtr format,
        out RectF bounds, out int codepointsFitted, out int linesFilled);

    [DllImport(Dll)] public static extern int GdipCreateHICONFromBitmap(IntPtr bitmap, out IntPtr hicon);

    public const int StringAlignmentNear = 0, StringAlignmentCenter = 1, StringAlignmentFar = 2;
    public const int FillModeAlternate = 0, CombineModeReplace = 0, WrapModeTileFlipXY = 3;
    public const int FontStyleItalic = 2, FontStyleStrikeout = 8;

    [DllImport(Dll)] public static extern int GdipSetStringFormatAlign(IntPtr format, int align);
    [DllImport(Dll)] public static extern int GdipSetStringFormatLineAlign(IntPtr format, int align);
    [DllImport(Dll)] public static extern int GdipCreatePath(int fillMode, out IntPtr path);
    [DllImport(Dll)] public static extern int GdipDeletePath(IntPtr path);
    [DllImport(Dll)] public static extern int GdipAddPathArc(IntPtr path, float x, float y, float w, float h, float start, float sweep);
    [DllImport(Dll)] public static extern int GdipAddPathRectangle(IntPtr path, float x, float y, float w, float h);
    [DllImport(Dll)] public static extern int GdipClosePathFigure(IntPtr path);
    [DllImport(Dll)] public static extern int GdipFillPath(IntPtr graphics, IntPtr brush, IntPtr path);
    [DllImport(Dll)] public static extern int GdipDrawPath(IntPtr graphics, IntPtr pen, IntPtr path);
    [DllImport(Dll)] public static extern int GdipCreatePen1(uint argb, float width, int unit, out IntPtr pen);
    [DllImport(Dll)] public static extern int GdipDeletePen(IntPtr pen);
    [DllImport(Dll)] public static extern int GdipDrawLine(IntPtr graphics, IntPtr pen, float x1, float y1, float x2, float y2);
    [DllImport(Dll)] public static extern int GdipCreateLineBrushFromRectWithAngle(ref RectF rect, uint color1, uint color2, float angle, int isAngleScalable, int wrapMode, out IntPtr brush);
    [DllImport(Dll)] public static extern int GdipSetClipRect(IntPtr graphics, float x, float y, float w, float h, int combineMode);
    [DllImport(Dll)] public static extern int GdipResetClip(IntPtr graphics);
    [DllImport(Dll)] public static extern int GdipDrawImageI(IntPtr graphics, IntPtr image, int x, int y);

    /// <summary>A rounded rectangle path (radius 0 = plain rectangle). Delete with GdipDeletePath.</summary>
    public static IntPtr RoundRect(float x, float y, float w, float h, float r)
    {
        GdipCreatePath(FillModeAlternate, out var path);
        if (r <= 0.5f || w < 2 * r || h < 2 * r)
        {
            GdipAddPathRectangle(path, x, y, w, h);
            return path;
        }
        float d = 2 * r;
        GdipAddPathArc(path, x, y, d, d, 180, 90);
        GdipAddPathArc(path, x + w - d, y, d, d, 270, 90);
        GdipAddPathArc(path, x + w - d, y + h - d, d, d, 0, 90);
        GdipAddPathArc(path, x, y + h - d, d, d, 90, 90);
        GdipClosePathFigure(path);
        return path;
    }

    public static void FillRoundRect(IntPtr g, uint argb, float x, float y, float w, float h, float r)
    {
        GdipCreateSolidFill(argb, out var brush);
        var path = RoundRect(x, y, w, h, r);
        GdipFillPath(g, brush, path);
        GdipDeletePath(path);
        GdipDeleteBrush(brush);
    }

    public static void DrawRoundRect(IntPtr g, uint argb, float width, float x, float y, float w, float h, float r)
    {
        GdipCreatePen1(argb, width, UnitPixel, out var pen);
        var path = RoundRect(x, y, w, h, r);
        GdipDrawPath(g, pen, path);
        GdipDeletePath(path);
        GdipDeletePen(pen);
    }
}
