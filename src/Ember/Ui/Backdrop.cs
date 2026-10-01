using System.Runtime.InteropServices;
using Ember.Core.Render;
using Ember.Native;

namespace Ember.Ui;

/// <summary>
/// Widget backgrounds. "acrylic", "blur" and "glass" use the system blur behind the window
/// (SetWindowCompositionAttribute accent policy, as Rainmeter and TranslucentTB do) with a rounded
/// window region; the other kinds are painted by GDI+ into the layered bitmap.
/// </summary>
internal static class Backdrop
{
    public static bool UsesSystemBlur(string kind) => kind is "acrylic" or "blur" or "glass";

    /// <summary>Sets the accent policy (blur/acrylic or none) and the rounded region.</summary>
    public static void Apply(IntPtr hwnd, BackgroundTheme bg, int width, int height, float scale)
    {
        var tint = Rgb.Parse(bg.Color, Palette.Background);
        int state = bg.Kind switch
        {
            "acrylic" => W.ACCENT_ENABLE_ACRYLICBLURBEHIND,
            "blur" or "glass" => W.ACCENT_ENABLE_BLURBEHIND,
            _ => W.ACCENT_DISABLED,
        };
        // Acrylic tint is AABBGGRR; it needs alpha > 0. The tint stays light: the opacity slider
        // controls the colour layer that Paint draws over the blur (so it changes the real opacity).
        const byte a = 0x18;
        var accent = new ACCENT_POLICY { AccentState = state, AccentFlags = state == W.ACCENT_ENABLE_ACRYLICBLURBEHIND ? 2 : 0, GradientColor = ((uint)a << 24) | tint.ToColorRef() };
        int size = Marshal.SizeOf<ACCENT_POLICY>();
        IntPtr p = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, p, false);
            var data = new WINDOWCOMPOSITIONATTRIBDATA { Attrib = W.WCA_ACCENT_POLICY, pvData = p, cbData = size };
            W.SetWindowCompositionAttribute(hwnd, ref data);
        }
        catch (EntryPointNotFoundException) { }
        finally { Marshal.FreeHGlobal(p); }

        // No system shadow or square corners from DWM (Windows 11 draws them for blurred windows).
        int policy = W.DWMNCRP_DISABLED;
        int corner = bg.CornerRadius > 0 ? (bg.CornerRadius <= 4 ? W.DWMWCP_ROUNDSMALL : W.DWMWCP_ROUND) : W.DWMWCP_DONOTROUND;
        try
        {
            W.DwmSetWindowAttribute(hwnd, W.DWMWA_NCRENDERING_POLICY, ref policy, sizeof(int));
            W.DwmSetWindowAttribute(hwnd, W.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }

        if (state != W.ACCENT_DISABLED && width > 0 && height > 0)
        {
            int d = (int)Math.Round(2 * bg.CornerRadius * scale);
            W.SetWindowRgn(hwnd, d > 0 ? W.CreateRoundRectRgn(0, 0, width + 1, height + 1, d, d) : IntPtr.Zero, true);
            // The system blur of a layered window can ignore the window region: also give DWM the
            // rounded blur region, so the blur is cut at the corners too.
            ClipBlur(hwnd, d > 0 ? W.CreateRoundRectRgn(0, 0, width + 1, height + 1, d, d) : IntPtr.Zero);
        }
        else
        {
            W.SetWindowRgn(hwnd, IntPtr.Zero, true);
            ClipBlur(hwnd, IntPtr.Zero);
        }
    }

    private static void ClipBlur(IntPtr hwnd, IntPtr region)
    {
        try
        {
            var bb = new DWM_BLURBEHIND { dwFlags = W.DWM_BB_ENABLE | W.DWM_BB_BLURREGION, fEnable = region != IntPtr.Zero ? 1 : 0, hRgnBlur = region };
            W.DwmEnableBlurBehindWindow(hwnd, ref bb);
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        finally
        {
            if (region != IntPtr.Zero) W.DeleteObject(region); // DWM copies the region
        }
    }

    /// <summary>Paints the background into the bitmap (the system blur, if any, is behind it).</summary>
    public static void Paint(IntPtr g, BackgroundTheme bg, float w, float h, float scale)
    {
        if (bg.Kind == "none") return;
        Gdip.GdipSetSmoothingMode(g, Gdip.SmoothingModeAntiAlias);
        float r = bg.CornerRadius * scale;
        var c1 = Rgb.Parse(bg.Color, Palette.Background);
        var c2 = Rgb.Parse(bg.Color2, Palette.Background);
        byte a = (byte)Math.Clamp(bg.Opacity * 255 / 100, 0, 255);
        switch (bg.Kind)
        {
            case "solid":
            case "panel":
                Gdip.FillRoundRect(g, c1.ToArgb(a), 0, 0, w, h, r);
                break;
            case "gradient":
            {
                var rect = new RectF(0, 0, w, h);
                Gdip.GdipCreateLineBrushFromRectWithAngle(ref rect, c1.ToArgb(a), c2.ToArgb(a), bg.GradientAngle, 1, Gdip.WrapModeTileFlipXY, out var brush);
                var path = Gdip.RoundRect(0, 0, w, h, r);
                Gdip.GdipFillPath(g, brush, path);
                Gdip.GdipDeletePath(path);
                Gdip.GdipDeleteBrush(brush);
                break;
            }
            case "acrylic":
            case "blur":
                // Colour layer over the system blur: the opacity slider sets it (at least 4, so the
                // whole area stays hit-testable in edit mode).
                Gdip.FillRoundRect(g, c1.ToArgb(Math.Max(a, (byte)4)), 0, 0, w, h, r);
                break;
            case "glass":
            {
                Gdip.FillRoundRect(g, c1.ToArgb(a), 0, 0, w, h, r);
                // Soft highlight from the top (second colour), fading out by the middle.
                var rect = new RectF(0, 0, w, h * 0.55f);
                Gdip.GdipCreateLineBrushFromRectWithAngle(ref rect, c2.ToArgb((byte)Math.Min(255, 40 + a / 6)), c2.ToArgb(0), 90, 1, Gdip.WrapModeTileFlipXY, out var brush);
                var path = Gdip.RoundRect(0, 0, w, h * 0.55f, r);
                Gdip.GdipFillPath(g, brush, path);
                Gdip.GdipDeletePath(path);
                Gdip.GdipDeleteBrush(brush);
                break;
            }
        }
        if (bg.BorderOpacity > 0)
        {
            var border = Rgb.Parse(bg.Border, Palette.Dim);
            float bw = Math.Max(1, scale);
            Gdip.DrawRoundRect(g, border.ToArgb((byte)(bg.BorderOpacity * 255 / 100)), bw, bw / 2, bw / 2, w - bw, h - bw, Math.Max(0, r - bw / 2));
        }
    }
}
