using System.Diagnostics;
using System.Runtime.InteropServices;
using Ember.App;
using Ember.Core.Config;
using Ember.Core.Render;
using Ember.Native;

namespace Ember.Ui;

/// <summary>
/// The status widget: a borderless, per-pixel transparent (layered) window on the desktop layer.
/// It is owned by the desktop window (Progman), so it is not topmost and stays visible on "Show desktop".
/// Clicks go through it (WS_EX_TRANSPARENT), except on buttons/process lines and in edit mode.
/// Size changes are animated (height eases to the new value).
/// </summary>
internal sealed class WidgetWindow : Win32Window
{
    private const string ClassName = "Ember.Widget";
    private const uint HoverTimer = 1, AnimTimer = 2;

    private readonly TextRenderer _text = new();
    private List<Line> _lines = new();
    private string _lastKey = "";
    private readonly List<(RECT Rect, Hit Hit)> _hits = new();
    private int _hover = -1;
    private bool _transparent = true;
    private bool _hoverTimer;
    private IntPtr _dc, _bitmap, _oldBitmap, _bits;
    private int _bmpW, _bmpH;
    private uint _dpi = 96;
    private POINT _pos;
    private SIZE _shown;          // size on screen now
    private int _targetH, _targetW;
    private int _animFromH;
    private readonly Stopwatch _anim = new();
    private bool _animating, _moving, _noTipUntilMove;
    private string _accentKey = "";
    private float _pad;

    private EmberSettings _settings = new();
    private string _themeKey = "";

    public EmberSettings Settings
    {
        get => _settings;
        set
        {
            _settings = value;
            _themeKey = ThemeStore.Serialize(value.Theme);
        }
    }
    public bool EditMode { get; private set; }
    public bool Hidden { get; private set; }

    /// <summary>A click on a button or line hit (hit, screen point).</summary>
    public event Action<Hit, POINT>? HitClicked;
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
        if (!Hidden) W.ShowWindow(Handle, W.SW_SHOWNOACTIVATE);
        W.SetWindowPos(Handle, W.HWND_BOTTOM, 0, 0, 0, 0, W.SWP_NOMOVE | W.SWP_NOSIZE | W.SWP_NOACTIVATE);
        _accentKey = "";
        Invalidate();
    }

    public void BringToFront()
    {
        if (!IsAlive) return;
        SetHidden(false);
        W.SetWindowPos(Handle, W.HWND_TOP, 0, 0, 0, 0, W.SWP_NOMOVE | W.SWP_NOSIZE | W.SWP_NOACTIVATE | W.SWP_SHOWWINDOW);
    }

    /// <summary>Hide (the tray icon shows it again).</summary>
    public void SetHidden(bool hidden)
    {
        if (!IsAlive || hidden == Hidden) return;
        Hidden = hidden;
        Tooltip.HideShared();
        StopAnim();
        UpdateHoverTimer();
        W.ShowWindow(Handle, hidden ? W.SW_HIDE : W.SW_SHOWNOACTIVATE);
        if (!hidden) Invalidate();
    }

    public void SetEditMode(bool on)
    {
        EditMode = on;
        SetTransparent(!on);
        UpdateHoverTimer();
        Invalidate();
    }

    /// <summary>Force a redraw (settings or DPI changed).</summary>
    public void Invalidate()
    {
        _lastKey = "";
        Render(_lines);
    }

    /// <summary>Show new lines. Redraws only if the text, colours or layout changed.</summary>
    public void Render(List<Line> lines)
    {
        if (!IsAlive) return;
        _lines = lines;
        if (Hidden) return;
        var mon = Monitors.Get(Settings.Layout.Monitor);
        _dpi = Monitors.Dpi(mon.Handle);
        var lay = Settings.Layout;
        var bg = Settings.Theme.Background;
        string key = string.Join("\n", lines.Select(l => l.Key)) +
                     $"|{lay.Font}|{lay.FontSize}|{lay.Opacity}|{lay.Corner}|{lay.OffsetX}|{lay.OffsetY}|{lay.Monitor}|{_dpi}|{EditMode}|{_hover}" +
                     $"|{_themeKey}|{mon.Work.Left},{mon.Work.Top},{mon.Work.Right},{mon.Work.Bottom}";
        if (key == _lastKey) return;
        _lastKey = key;

        _text.Configure(lay.Font, lay.FontSize, _dpi);
        float scale = _dpi / 96f;
        _pad = Math.Max(2, bg.Padding * scale);
        _targetW = (int)Math.Ceiling(Layout.Width * _text.CellWidth + 2 * _pad);
        int newH = (int)Math.Ceiling(Math.Max(1, lines.Count) * _text.LineHeight + 2 * _pad);
        if (newH != _targetH && Settings.DebugLog && Settings.DebugDecisions)
            App.Log.Write($"decide: window height {_targetH} → {newH} px ({lines.Count} lines){(_shown.CY > 0 && Settings.Layout.Animate ? ", animated" : "")}");
        _targetH = newH;

        BuildHits(lines);
        UpdateHoverTimer();

        bool animate = lay.Animate && lay.AnimationMs > 0 && _shown.CY > 0 && _shown.CY != _targetH && !_moving && _shown.CX == _targetW;
        if (animate)
        {
            _animFromH = _animating ? CurrentAnimHeight() : _shown.CY;
            _anim.Restart();
            if (!_animating)
            {
                _animating = true;
                W.SetTimer(Handle, (UIntPtr)AnimTimer, 15, IntPtr.Zero);
            }
            DrawFrame(CurrentAnimHeight());
        }
        else
        {
            StopAnim();
            DrawFrame(_targetH);
        }
    }

    private int CurrentAnimHeight()
    {
        double t = Math.Clamp(_anim.Elapsed.TotalMilliseconds / Math.Max(1, Settings.Layout.AnimationMs), 0, 1);
        double e = 1 - Math.Pow(1 - t, 3); // ease-out cubic
        return (int)Math.Round(_animFromH + (_targetH - _animFromH) * e);
    }

    private void StopAnim()
    {
        if (!_animating) return;
        _animating = false;
        W.KillTimer(Handle, (UIntPtr)AnimTimer);
    }

    private void BuildHits(List<Line> lines)
    {
        _hits.Clear();
        for (int i = 0; i < lines.Count; i++)
        {
            int top = (int)(_pad + i * _text.LineHeight);
            foreach (var h in lines[i].Hits)
            {
                int left = h.Width >= Layout.Width ? 0 : (int)(_pad + h.Col * _text.CellWidth);
                int right = h.Width >= Layout.Width ? _targetW : (int)Math.Ceiling(_pad + (h.Col + h.Width) * _text.CellWidth);
                _hits.Add((new RECT(left, top, right, (int)Math.Ceiling(top + _text.LineHeight)), h));
            }
        }
        if (_hover >= _hits.Count) _hover = -1;
    }

    /// <summary>Draws background and text at the given height and shows it.</summary>
    private void DrawFrame(int h)
    {
        int w = _targetW;
        h = Math.Max(1, h);
        var lay = Settings.Layout;
        var bg = Settings.Theme.Background;
        float scale = _dpi / 96f;

        EnsureBitmap(w, h);
        Gdip.GdipCreateBitmapFromScan0(w, h, w * 4, Gdip.PixelFormat32bppPARGB, _bits, out var bmp);
        Gdip.GdipGetImageGraphicsContext(bmp, out var g);
        try
        {
            Gdip.GdipGraphicsClear(g, 0x00000000);
            Backdrop.Paint(g, bg, w, h, scale);
            if (EditMode) Gdip.FillRoundRect(g, Palette.Background.ToArgb(0x90), 0, 0, w, h, bg.CornerRadius * scale);

            // Hit areas: almost invisible fill (alpha 3) so the whole area receives the mouse (alpha-0 pixels never do).
            if (!EditMode && _hits.Count > 0)
            {
                Gdip.GdipCreateSolidFill(0x03000000, out var hitBrush);
                foreach (var (r, _) in _hits)
                    if (r.Top < h) Gdip.GdipFillRectangle(g, hitBrush, r.Left, r.Top, r.Width, Math.Min(r.Height, h - r.Top));
                Gdip.GdipDeleteBrush(hitBrush);
            }
            // Hovered button: soft highlight.
            if (_hover >= 0 && _hover < _hits.Count && _hits[_hover].Hit.Width < Layout.Width)
            {
                var r = _hits[_hover].Rect;
                Gdip.FillRoundRect(g, new Rgb(0xFF, 0xFF, 0xFF).ToArgb(0x28), r.Left - 1, r.Top, r.Width + 2, r.Height, 3 * scale);
            }

            Gdip.GdipSetClipRect(g, 0, 0, w, Math.Max(0, h - _pad / 2), Gdip.CombineModeReplace);
            byte alpha = (byte)Math.Round(255 * Math.Clamp(lay.Opacity, 10, 100) / 100.0);
            _text.Draw(g, _lines, _pad, _pad, alpha, bg.TextShadow, h, Settings.Theme.ChipRadius / 100f);
            Gdip.GdipResetClip(g);
        }
        finally
        {
            Gdip.GdipDeleteGraphics(g);
            Gdip.GdipDisposeImage(bmp);
        }

        var mon = Monitors.Get(lay.Monitor);
        if (_moving && W.GetWindowRect(Handle, out var cur)) _pos = new POINT(cur.Left, cur.Top);
        else _pos = Position(mon, w, h, scale);
        _shown = new SIZE(w, h);

        string accentKey = $"{bg.Kind}|{bg.Color}|{bg.Opacity}|{bg.CornerRadius}|{w}x{h}|{_dpi}";
        if (accentKey != _accentKey)
        {
            _accentKey = accentKey;
            Backdrop.Apply(Handle, bg, w, h, scale);
        }

        var blend = new BLENDFUNCTION { BlendOp = W.AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = W.AC_SRC_ALPHA };
        var src = new POINT(0, 0);
        var screen = W.GetDC(IntPtr.Zero);
        W.UpdateLayeredWindow(Handle, screen, ref _pos, ref _shown, _dc, ref src, 0, ref blend, W.ULW_ALPHA);
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
    /// While buttons or process lines are visible, a 100 ms timer checks the cursor: it removes
    /// WS_EX_TRANSPARENT only while the cursor is over a hit, highlights the hovered button and shows its tooltip.
    /// </summary>
    private void UpdateHoverTimer()
    {
        bool need = !EditMode && _hits.Count > 0 && !Hidden;
        if (need && !_hoverTimer)
        {
            W.SetTimer(Handle, (UIntPtr)HoverTimer, 100, IntPtr.Zero);
            _hoverTimer = true;
        }
        else if (!need && _hoverTimer)
        {
            W.KillTimer(Handle, (UIntPtr)HoverTimer);
            _hoverTimer = false;
            SetHover(-1, default);
            if (!EditMode) SetTransparent(true);
        }
    }

    /// <summary>Index of the smallest hit under the window point, or -1.</summary>
    private int HitAt(int x, int y)
    {
        int best = -1, bestW = int.MaxValue;
        for (int i = 0; i < _hits.Count; i++)
        {
            var r = _hits[i].Rect;
            if (r.Contains(x, y) && r.Width < bestW && y < _shown.CY)
            {
                best = i;
                bestW = r.Width;
            }
        }
        return best;
    }

    private int HitAtScreen(POINT p) => HitAt(p.X - _pos.X, p.Y - _pos.Y);

    private void SetHover(int index, POINT cursor)
    {
        // No tooltips while a mouse button is down (drag) or right after a drag, until the cursor goes to another hit.
        bool button = (W.GetAsyncKeyState(W.VK_LBUTTON) & 0x8000) != 0;
        if (_moving || button) _noTipUntilMove = true;
        else if (index != _hover) _noTipUntilMove = false;
        if (!_noTipUntilMove && index >= 0 && _hits[index].Hit.Tooltip is { Length: > 0 } tip) Tooltip.Shared.Show(tip, cursor);
        else Tooltip.HideShared();
        if (index == _hover) return;
        _hover = index;
        Invalidate();
    }

    protected override IntPtr OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case W.WM_TIMER when (uint)wParam == HoverTimer:
                if (Hidden) return IntPtr.Zero;
                if (W.GetCursorPos(out var cp))
                {
                    int hit = HitAtScreen(cp);
                    SetTransparent(hit < 0);
                    SetHover(hit, cp);
                }
                return IntPtr.Zero;

            case W.WM_TIMER when (uint)wParam == AnimTimer:
            {
                int h = CurrentAnimHeight();
                if (h == _targetH || _anim.ElapsedMilliseconds >= Settings.Layout.AnimationMs) { StopAnim(); h = _targetH; }
                DrawFrame(h);
                return IntPtr.Zero;
            }

            case W.WM_NCHITTEST:
            {
                // Edit mode: drag anywhere except on buttons. Always: drag by the top row.
                var p = new POINT(W.LoWord(lParam), W.HiWord(lParam));
                int i = HitAtScreen(p);
                if (i >= 0 && _hits[i].Hit.Action == HitAction.Drag) return (IntPtr)W.HTCAPTION;
                if (EditMode) return (IntPtr)(i >= 0 ? W.HTCLIENT : W.HTCAPTION);
                return (IntPtr)W.HTCLIENT;
            }

            case W.WM_SETCURSOR:
                if (W.GetCursorPos(out var sp) && HitAtScreen(sp) is var hi && hi >= 0 && _hits[hi].Hit.Action != HitAction.Drag)
                {
                    W.SetCursor(W.LoadCursor(IntPtr.Zero, W.IDC_HAND));
                    return (IntPtr)1;
                }
                break;

            case W.WM_LBUTTONUP:
            {
                int i = HitAt(W.LoWord(lParam), W.HiWord(lParam));
                if (i >= 0 && W.GetCursorPos(out var pt))
                {
                    Tooltip.HideShared();
                    HitClicked?.Invoke(_hits[i].Hit, pt);
                }
                return IntPtr.Zero;
            }

            case W.WM_RBUTTONUP:
            {
                int i = HitAt(W.LoWord(lParam), W.HiWord(lParam));
                W.GetCursorPos(out var pt);
                Tooltip.HideShared();
                if (i >= 0 && _hits[i].Hit.Action is HitAction.ProcessDetails or HitAction.ProcessKill or HitAction.ProcessProperties
                    && int.TryParse(_hits[i].Hit.Arg, out int pid))
                    ProcessMenu?.Invoke(pid, pt);
                else if (EditMode) WidgetMenu?.Invoke(pt);
                return IntPtr.Zero;
            }

            case W.WM_NCRBUTTONUP:
                // Right click on the top row (or anywhere in edit mode): widget menu.
                if (W.GetCursorPos(out var np)) WidgetMenu?.Invoke(np);
                return IntPtr.Zero;

            case W.WM_NCLBUTTONDOWN:
                _noTipUntilMove = true;
                Tooltip.HideShared();
                break;

            case 0x0231: // WM_ENTERSIZEMOVE
                _moving = true;
                _noTipUntilMove = true;
                Tooltip.HideShared();
                StopAnim();
                return IntPtr.Zero;

            case W.WM_EXITSIZEMOVE:
                _moving = false;
                if (W.GetWindowRect(Handle, out var moved)) _pos = new POINT(moved.Left, moved.Top);
                SavePosition();
                return IntPtr.Zero;

            case W.WM_DPICHANGED:
            case W.WM_DISPLAYCHANGE:
                Invalidate();
                return IntPtr.Zero;

            case W.WM_DESTROY:
                if (_hoverTimer) W.KillTimer(Handle, (UIntPtr)HoverTimer);
                StopAnim();
                FreeBitmap();
                _text.Dispose();
                return IntPtr.Zero;
        }
        return base.OnMessage(msg, wParam, lParam);
    }
}
