using System.Runtime.InteropServices;
using Ember.App;
using Ember.Core.Render;
using Ember.Native;

namespace Ember.Ui;

/// <summary>
/// A small tooltip that shows at once (no hover delay): a layered, click-through, topmost popup
/// drawn with GDI+. One shared instance for the widget and the notes.
/// </summary>
internal sealed class Tooltip : Win32Window
{
    private const string ClassName = "Ember.Tooltip";
    private static Tooltip? _shared;
    private string _text = "";
    private POINT _at;
    private SIZE _size;
    private float _scale = 1;

    public static Tooltip Shared
    {
        get
        {
            if (_shared is not { IsAlive: true })
            {
                _shared = new Tooltip();
                _shared.Create();
            }
            return _shared;
        }
    }

    private void Create()
    {
        RegisterClass(ClassName, IntPtr.Zero, IntPtr.Zero);
        CreateHandle(ClassName, null, W.WS_EX_LAYERED | W.WS_EX_TRANSPARENT | W.WS_EX_TOOLWINDOW | W.WS_EX_NOACTIVATE | W.WS_EX_TOPMOST,
            W.WS_POPUP, 0, 0, 1, 1, IntPtr.Zero);
    }

    /// <summary>Shows the text below-right of the screen point. Same text at the same point: no redraw.</summary>
    public void Show(string text, POINT at)
    {
        if (!IsAlive || string.IsNullOrEmpty(text)) { Hide(); return; }
        if (text == _text && W.IsWindowVisible(Handle))
        {
            // Same text: only follow the cursor (no new drawing), and not for small moves.
            if (Math.Abs(at.X - _at.X) + Math.Abs(at.Y - _at.Y) < 3) return;
            _at = at;
            var p = Place(at, _size.CX, _size.CY, _scale);
            W.SetWindowPos(Handle, new IntPtr(-1), p.X, p.Y, 0, 0, W.SWP_NOSIZE | W.SWP_NOACTIVATE);
            return;
        }
        _text = text;
        _at = at;
        var mon = W.MonitorFromPoint(at, W.MONITOR_DEFAULTTONEAREST);
        float scale = Monitors.Dpi(mon) / 96f;
        float em = 12 * scale, pad = 6 * scale;

        Gdip.GdipCreateFontFamilyFromName("Segoe UI", IntPtr.Zero, out var family);
        Gdip.GdipCreateFont(family, em, Gdip.FontStyleRegular, Gdip.UnitPixel, out var font);
        Gdip.GdipStringFormatGetGenericTypographic(out var fmt);
        Gdip.GdipCreateBitmapFromScan0(1, 1, 0, Gdip.PixelFormat32bppPARGB, IntPtr.Zero, out var probe);
        Gdip.GdipGetImageGraphicsContext(probe, out var pg);
        var layout = new RectF(0, 0, 600 * scale, 400 * scale);
        Gdip.GdipMeasureString(pg, text, text.Length, font, ref layout, fmt, out var box, out _, out _);
        Gdip.GdipDeleteGraphics(pg);
        Gdip.GdipDisposeImage(probe);

        int w = (int)Math.Ceiling(box.Width + 2 * pad), h = (int)Math.Ceiling(box.Height + 2 * pad);
        var bmi = new BITMAPINFOHEADER { biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var dc = W.CreateCompatibleDC(IntPtr.Zero);
        var dib = W.CreateDIBSection(dc, ref bmi, 0, out var bits, IntPtr.Zero, 0);
        var old = W.SelectObject(dc, dib);
        Gdip.GdipCreateBitmapFromScan0(w, h, w * 4, Gdip.PixelFormat32bppPARGB, bits, out var bmp);
        Gdip.GdipGetImageGraphicsContext(bmp, out var g);
        Gdip.GdipGraphicsClear(g, 0);
        Gdip.GdipSetSmoothingMode(g, Gdip.SmoothingModeAntiAlias);
        Gdip.FillRoundRect(g, new Rgb(0x20, 0x20, 0x24).ToArgb(0xF0), 0, 0, w, h, 5 * scale);
        Gdip.DrawRoundRect(g, new Rgb(0x60, 0x60, 0x68).ToArgb(0xC0), 1, 0.5f, 0.5f, w - 1, h - 1, 5 * scale);
        Gdip.GdipSetTextRenderingHint(g, Gdip.TextRenderingHintAntiAliasGridFit);
        Gdip.GdipCreateSolidFill(new Rgb(0xEE, 0xEE, 0xEE).ToArgb(), out var brush);
        var rect = new RectF(pad, pad, box.Width + 1, box.Height + 1);
        Gdip.GdipDrawString(g, text, text.Length, font, ref rect, fmt, brush);
        Gdip.GdipDeleteBrush(brush);
        Gdip.GdipDeleteGraphics(g);
        Gdip.GdipDisposeImage(bmp);
        Gdip.GdipDeleteFont(font);
        Gdip.GdipDeleteFontFamily(family);

        _scale = scale;
        _size = new SIZE(w, h);
        var pos = Place(at, w, h, scale);
        var size = new SIZE(w, h);
        var src = new POINT(0, 0);
        var blend = new BLENDFUNCTION { BlendOp = W.AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = W.AC_SRC_ALPHA };
        var screen = W.GetDC(IntPtr.Zero);
        W.UpdateLayeredWindow(Handle, screen, ref pos, ref size, dc, ref src, 0, ref blend, W.ULW_ALPHA);
        W.ReleaseDC(IntPtr.Zero, screen);
        W.SelectObject(dc, old);
        W.DeleteObject(dib);
        W.DeleteDC(dc);
        W.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, W.SWP_NOMOVE | W.SWP_NOSIZE | W.SWP_NOACTIVATE | W.SWP_SHOWWINDOW);
    }

    /// <summary>Below-right of the point, inside the monitor's work area.</summary>
    private static POINT Place(POINT at, int w, int h, float scale)
    {
        var mon = W.MonitorFromPoint(at, W.MONITOR_DEFAULTTONEAREST);
        var work = Monitors.All().FirstOrDefault(m => m.Handle == mon).Work;
        int x = at.X + (int)(14 * scale), y = at.Y + (int)(18 * scale);
        if (work.Width > 0)
        {
            x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - w));
            if (y + h > work.Bottom) y = at.Y - h - (int)(6 * scale);
        }
        return new POINT(x, y);
    }

    public void Hide()
    {
        _text = "";
        if (IsAlive) W.ShowWindow(Handle, W.SW_HIDE);
    }

    /// <summary>Hides the shared tooltip if it exists.</summary>
    public static void HideShared()
    {
        if (_shared is { IsAlive: true }) _shared.Hide();
    }
}
