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
    [DllImport(Dll)] public static extern int GdipCloneStringFormat(IntPtr format, out IntPtr clone);
    [DllImport(Dll)] public static extern int GdipSetStringFormatFlags(IntPtr format, int flags);
    [DllImport(Dll)] public static extern int GdipDeleteStringFormat(IntPtr format);

    [DllImport(Dll, CharSet = CharSet.Unicode)]
    public static extern int GdipDrawString(IntPtr graphics, string text, int length, IntPtr font, ref RectF layout, IntPtr format, IntPtr brush);
    [DllImport(Dll, CharSet = CharSet.Unicode)]
    public static extern int GdipMeasureString(IntPtr graphics, string text, int length, IntPtr font, ref RectF layout, IntPtr format,
        out RectF bounds, out int codepointsFitted, out int linesFilled);

    [DllImport(Dll)] public static extern int GdipCreateHICONFromBitmap(IntPtr bitmap, out IntPtr hicon);
}
