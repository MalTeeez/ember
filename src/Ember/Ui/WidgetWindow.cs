using System.Runtime.InteropServices;
using Ember.App;
using Ember.Core.Config;
using Ember.Core.Render;
using Ember.Native;

namespace Ember.Ui;

/// <summary>
/// The status widget: a borderless, per-pixel transparent (layered) window on the desktop layer.
/// It is owned by the desktop window (Progman), so it is not topmost and stays visible on "Show desktop".
/// Clicks go through it (WS_EX_TRANSPARENT), except on top-process lines and in edit mode.
/// </summary>
internal sealed class WidgetWindow : Win32Window
{
    private const string ClassName = "Ember.Widget";
    private const uint HoverTimer = 1;
    private const float PadDip = 6;

    private readonly TextRenderer _text = new();
    private List<Line> _lines = new();
    private string _lastKey = "";
    private readonly List<(RECT Rect, int Pid)> _targets = new();
    private bool _transparent = true;
    private bool _hoverTimer;
    private IntPtr _dc, _bitmap, _oldBitmap, _bits;
    private int _bmpW, _bmpH;
    private uint _dpi = 96;
    private POINT _pos;
    private SIZE _size;
    private bool _moving;

    public EmberSettings Settings { get; set; } = new();
    public bool EditMode { get; private set; }

    /// <summary>Left click on a process line (pid, screen point).</summary>
    public event Action<int, POINT>? ProcessClicked;
    /// <summary>Right click on a process line.</summary>
    public event Action<int, POINT>? ProcessMenu;
    /// <summary>Right click on the widget in edit mode.</summary>
    public event Action<POINT>? WidgetMenu;
    /// <summary>The window was moved in edit mode; settings changed.</summary>
    public event Action? Moved;

    public void Create()
    {
        RegisterClass(ClassName, IntPtr.Zero, IntPtr.Zero);
        uint ex = W.WS_EX_LAYERED | W.WS_EX_TOOLWINDOW | W.WS_EX_NOACTIVATE | W.WS_EX_TRANSPARENT;
        CreateHandle(ClassName, "Ember", ex, W.WS_POPUP, 0, 0, 1, 1, DesktopOwner());
        _transparent = true;
        W.ShowWindow(Handle, W.SW_SHOWNOACTIVATE);
        W.SetWindowPos(Handle, W.HWND_BOTTOM, 0, 0, 0, 0, W.SWP_NOMOVE | W.SWP_NOSIZE | W.SWP_NOACTIVATE);
    }

    /// <summary>The desktop window (Progman). Zero if Explorer is not running.</summary>
    public static IntPtr DesktopOwner() => W.FindWindow("Progman", null);

    /// <summary>Attach to the desktop again (after an Explorer restart).</summary>
    public void Reattach()
    {
        if (!IsAlive) return;
        W.SetWindowLongPtr(Handle, W.GWLP_HWNDPARENT, DesktopOwner());
        W.ShowWindow(Handle, W.SW_SHOWNOACTIVATE);
        W.SetWindowPos(Handle, W.HWND_BOTTOM, 0, 0, 0, 0, W.SWP_NOMOVE | W.SWP_NOSIZE | W.SWP_NOACTIVATE);
        Invalidate();
    }

    /// <summary>Bring to the top of the z-order once (second instance start). It falls back as other windows activate.</summary>
    public void BringToFront()
    {
        if (!IsAlive) return;
        W.SetWindowPos(Handle, W.HWND_TOP, 0, 0, 0, 0, W.SWP_NOMOVE | W.SWP_NOSIZE | W.SWP_NOACTIVATE | W.SWP_SHOWWINDOW);
    }

    public void SetEditMode(bool on)
    {
        EditMode = on;
        SetTransparent(!on);
        UpdateHoverTimer();
        Invalidate();
    }

    /// <summary>Force a redraw on the next update (settings or DPI changed).</summary>
    public void Invalidate()
    {
        _lastKey = "";
        Render(_lines);
    }

    /// <summary>Show new lines. Redraws only if the text, colours or layout changed.</summary>
    public void Render(List<Line> lines)
    {
        if (!IsAlive) return;
        var mon = Monitors.Get(Settings.Layout.Monitor);
        _dpi = Monitors.Dpi(mon.Handle);
        var lay = Settings.Layout;
        string key = string.Join("\n", lines.Select(l => l.Key)) + $"|{lay.Font}|{lay.FontSize}|{lay.Opacity}|{lay.Corner}|{lay.OffsetX}|{lay.OffsetY}|{lay.Monitor}|{_dpi}|{EditMode}|{mon.Work.Left},{mon.Work.Top},{mon.Work.Right},{mon.Work.Bottom}";
        _lines = lines;
        if (key == _lastKey) return;
        _lastKey = key;

        _text.Configure(lay.Font, lay.FontSize, _dpi);
        float scale = _dpi / 96f;
        float pad = PadDip * scale;
        int w = (int)Math.Ceiling(Layout.Width * _text.CellWidth + 2 * pad);
        int rows = Math.Max(1, lines.Count);
        int h = (int)Math.Ceiling(rows * _text.LineHeight + 2 * pad);

        EnsureBitmap(w, h);
        Gdip.GdipCreateBitmapFromScan0(w, h, w * 4, Gdip.PixelFormat32bppPARGB, _bits, out var bmp);
        Gdip.GdipGetImageGraphicsContext(bmp, out var g);
        try
        {
            // Edit mode: a dark background so the window can be seen and dragged.
            Gdip.GdipGraphicsClear(g, EditMode ? Palette.Background.ToArgb(0xC0) : 0x00000000);
            _text.Draw(g, lines, pad, pad);
        }
        finally
        {
            Gdip.GdipDeleteGraphics(g);
            Gdip.GdipDisposeImage(bmp);
        }

        // Click targets (window coordinates).
        _targets.Clear();
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Click != ClickKind.Process) continue;
            int top = (int)(pad + i * _text.LineHeight);
            _targets.Add((new RECT(0, top, w, (int)(top + _text.LineHeight)), lines[i].Pid));
        }
        UpdateHoverTimer();

        _size = new SIZE(w, h);
        if (_moving && W.GetWindowRect(Handle, out var cur)) _pos = new POINT(cur.Left, cur.Top);
        else _pos = Position(mon, w, h, scale);
        var blend = new BLENDFUNCTION
        {
            BlendOp = W.AC_SRC_OVER,
            SourceConstantAlpha = (byte)Math.Round(255 * Math.Clamp(lay.Opacity, 10, 100) / 100.0),
            AlphaFormat = W.AC_SRC_ALPHA,
        };
        var src = new POINT(0, 0);
        var screen = W.GetDC(IntPtr.Zero);
        W.UpdateLayeredWindow(Handle, screen, ref _pos, ref _size, _dc, ref src, 0, ref blend, W.ULW_ALPHA);
        W.ReleaseDC(IntPtr.Zero, screen);
    }

    private POINT Position(Monitors.Info mon, int w, int h, float scale)
    {
        var lay = Settings.Layout;
        int ox = (int)Math.Round(lay.OffsetX * scale), oy = (int)Math.Round(lay.OffsetY * scale);
        var wa = mon.Work;
        int x = lay.Corner is Corner.TopLeft or Corner.BottomLeft ? wa.Left + ox : wa.Right - ox - w;
        int y = lay.Corner is Corner.TopLeft or Corner.TopRight ? wa.Top + oy : wa.Bottom - oy - h;
        return new POINT(x, y);
    }

    /// <summary>After a drag: store the offset from the selected corner of the monitor the window is on.</summary>
    private void SavePosition()
    {
        if (!W.GetWindowRect(Handle, out var r)) return;
        var hmon = W.MonitorFromWindow(Handle, W.MONITOR_DEFAULTTONEAREST);
        int index = Monitors.IndexOf(hmon);
        var mon = Monitors.Get(index);
        float scale = Monitors.Dpi(mon.Handle) / 96f;
        var lay = Settings.Layout;
        var wa = mon.Work;
        int ox = lay.Corner is Corner.TopLeft or Corner.BottomLeft ? r.Left - wa.Left : wa.Right - r.Right;
        int oy = lay.Corner is Corner.TopLeft or Corner.TopRight ? r.Top - wa.Top : wa.Bottom - r.Bottom;
        lay.Monitor = index;
        lay.OffsetX = (int)Math.Round(ox / scale);
        lay.OffsetY = (int)Math.Round(oy / scale);
        Moved?.Invoke();
    }

    private void EnsureBitmap(int w, int h)
    {
        if (_dc != IntPtr.Zero && w == _bmpW && h == _bmpH) return;
        FreeBitmap();
        var bmi = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = w,
            biHeight = -h, // top-down
            biPlanes = 1,
            biBitCount = 32,
        };
        _dc = W.CreateCompatibleDC(IntPtr.Zero);
        _bitmap = W.CreateDIBSection(_dc, ref bmi, 0, out _bits, IntPtr.Zero, 0);
        _oldBitmap = W.SelectObject(_dc, _bitmap);
        _bmpW = w;
        _bmpH = h;
    }

    private void FreeBitmap()
    {
        if (_dc == IntPtr.Zero) return;
        W.SelectObject(_dc, _oldBitmap);
        W.DeleteObject(_bitmap);
        W.DeleteDC(_dc);
        _dc = _bitmap = _bits = IntPtr.Zero;
    }

    private void SetTransparent(bool on)
    {
        if (_transparent == on || !IsAlive) return;
        _transparent = on;
        long ex = (long)W.GetWindowLongPtr(Handle, W.GWL_EXSTYLE);
        ex = on ? ex | W.WS_EX_TRANSPARENT : ex & ~(long)W.WS_EX_TRANSPARENT;
        W.SetWindowLongPtr(Handle, W.GWL_EXSTYLE, (IntPtr)ex);
    }

    /// <summary>
    /// While process lines are visible, a 100 ms timer checks the cursor position and removes
    /// WS_EX_TRANSPARENT only while the cursor is over a process line. No timer otherwise.
    /// </summary>
    private void UpdateHoverTimer()
    {
        bool need = !EditMode && _targets.Count > 0;
        if (need && !_hoverTimer)
        {
            W.SetTimer(Handle, (UIntPtr)HoverTimer, 100, IntPtr.Zero);
            _hoverTimer = true;
        }
        else if (!need && _hoverTimer)
        {
            W.KillTimer(Handle, (UIntPtr)HoverTimer);
            _hoverTimer = false;
            if (!EditMode) SetTransparent(true);
        }
    }

    private int TargetAt(int x, int y)
    {
        foreach (var (rect, pid) in _targets)
            if (rect.Contains(x, y)) return pid;
        return 0;
    }

    private int TargetAtScreen(POINT p) => TargetAt(p.X - _pos.X, p.Y - _pos.Y);

    protected override IntPtr OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case W.WM_TIMER when (uint)wParam == HoverTimer:
                if (W.GetCursorPos(out var cp)) SetTransparent(TargetAtScreen(cp) == 0);
                return IntPtr.Zero;

            case W.WM_NCHITTEST:
                if (EditMode)
                {
                    var p = new POINT(W.LoWord(lParam), W.HiWord(lParam));
                    return (IntPtr)(TargetAtScreen(p) != 0 ? W.HTCLIENT : W.HTCAPTION);
                }
                return (IntPtr)W.HTCLIENT;

            case W.WM_SETCURSOR:
                if (W.GetCursorPos(out var sp) && TargetAtScreen(sp) != 0)
                {
                    W.SetCursor(W.LoadCursor(IntPtr.Zero, W.IDC_HAND));
                    return (IntPtr)1;
                }
                break;

            case W.WM_LBUTTONUP:
            {
                int pid = TargetAt(W.LoWord(lParam), W.HiWord(lParam));
                if (pid != 0 && W.GetCursorPos(out var pt)) ProcessClicked?.Invoke(pid, pt);
                return IntPtr.Zero;
            }

            case W.WM_RBUTTONUP:
            {
                int pid = TargetAt(W.LoWord(lParam), W.HiWord(lParam));
                W.GetCursorPos(out var pt);
                if (pid != 0) ProcessMenu?.Invoke(pid, pt);
                else if (EditMode) WidgetMenu?.Invoke(pt);
                return IntPtr.Zero;
            }

            case W.WM_NCRBUTTONUP:
                if (EditMode && W.GetCursorPos(out var np)) WidgetMenu?.Invoke(np);
                return IntPtr.Zero;

            case 0x0231: // WM_ENTERSIZEMOVE
                _moving = true;
                return IntPtr.Zero;

            case W.WM_EXITSIZEMOVE:
                _moving = false;
                if (W.GetWindowRect(Handle, out var moved)) _pos = new POINT(moved.Left, moved.Top);
                SavePosition();
                return IntPtr.Zero;

            case W.WM_DPICHANGED:
                // Position and size come from the settings; just redraw for the new DPI.
                Invalidate();
                return IntPtr.Zero;

            case W.WM_DISPLAYCHANGE:
                Invalidate();
                return IntPtr.Zero;

            case W.WM_DESTROY:
                if (_hoverTimer) W.KillTimer(Handle, (UIntPtr)HoverTimer);
                FreeBitmap();
                _text.Dispose();
                return IntPtr.Zero;
        }
        return base.OnMessage(msg, wParam, lParam);
    }
}
