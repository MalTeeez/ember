using Ember.App;
using Ember.Core.Config;
using Ember.Native;

namespace Ember.Ui;

/// <summary>
/// Settings window, generated from <see cref="SettingsSchema"/>. Created when opened and destroyed
/// when closed. Every valid change applies at once and is saved to settings.json.
/// </summary>
internal sealed class SettingsWindow : Win32Window
{
    private const string ClassName = "Ember.Settings";
    private const int IdBase = 1000;
    private const uint WM_AUTOSTART_DONE = W.WM_APP + 20;

    private readonly EmberSettings _work;
    private readonly Action<EmberSettings> _apply;
    private readonly List<(SettingField Field, IntPtr Hwnd)> _controls = new();
    private IntPtr _font, _boldFont;
    private bool _loading;
    private int _scroll, _contentHeight;
    private float _scale = 1;
    private IntPtr _autostartBox;

    public event Action? Closed;

    public SettingsWindow(EmberSettings current, Action<EmberSettings> apply)
    {
        _work = SettingsStore.Clone(current);
        _apply = apply;
    }

    public void Create()
    {
        RegisterClass(ClassName, IntPtr.Zero, W.GetSysColorBrush(W.COLOR_BTNFACE));
        var mon = Monitors.Get(0);
        _scale = Monitors.Dpi(mon.Handle) / 96f;
        int w = S(460), h = Math.Min(S(720), mon.Work.Height - S(40));
        int x = mon.Work.Left + (mon.Work.Width - w) / 2, y = mon.Work.Top + (mon.Work.Height - h) / 2;
        CreateHandle(ClassName, "Ember settings", W.WS_EX_CONTROLPARENT,
            W.WS_OVERLAPPED | W.WS_CAPTION | W.WS_SYSMENU | W.WS_THICKFRAME | W.WS_MINIMIZEBOX | W.WS_VSCROLL | W.WS_CLIPCHILDREN,
            x, y, w, h, IntPtr.Zero);
        _scale = W.GetDpiForWindow(Handle) / 96f;
        _font = W.CreateFont(-S(12), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        _boldFont = W.CreateFont(-S(13), 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        BuildControls();
        W.ShowWindow(Handle, W.SW_SHOW);
        W.SetForegroundWindow(Handle);
        QueryAutostart();
    }

    private int S(double v) => (int)Math.Round(v * _scale);

    private void BuildControls()
    {
        _loading = true;
        W.GetClientRect(Handle, out var rc);
        int width = rc.Width;
        int y = S(8), left = S(12), ctrlX = S(260), ctrlW = width - ctrlX - S(16);
        string section = "";
        int id = IdBase;
        foreach (var f in SettingsSchema.Fields)
        {
            if (f.Section != section)
            {
                section = f.Section;
                y += S(6);
                var hdr = Child("STATIC", section, W.SS_LEFT, left, y, width - 2 * left, S(20), 0);
                W.SendMessage(hdr, W.WM_SETFONT, _boldFont, (IntPtr)1);
                y += S(24);
            }
            IntPtr ctrl;
            switch (f.Kind)
            {
                case FieldKind.Bool:
                    ctrl = Child("BUTTON", f.Label, W.BS_AUTOCHECKBOX | W.WS_TABSTOP, left, y, width - 2 * left, S(22), id);
                    if (!f.IsSpecial) W.SendMessage(ctrl, W.BM_SETCHECK, (IntPtr)(f.Get(_work) == "1" ? 1 : 0), IntPtr.Zero);
                    else _autostartBox = ctrl;
                    y += S(24);
                    break;
                case FieldKind.List:
                    Child("STATIC", f.Label, W.SS_LEFT, left, y, width - 2 * left, S(20), 0);
                    y += S(22);
                    ctrl = Child("EDIT", f.Get(_work), W.WS_TABSTOP | W.WS_BORDER | W.ES_MULTILINE | W.ES_AUTOVSCROLL | W.ES_WANTRETURN | W.WS_VSCROLL,
                        left, y, width - 2 * left, S(130), id);
                    y += S(138);
                    break;
                case FieldKind.Choice:
                    Child("STATIC", f.Label, W.SS_LEFT, left, y + S(3), ctrlX - left - S(8), S(20), 0);
                    ctrl = Child("COMBOBOX", null, W.WS_TABSTOP | W.CBS_DROPDOWNLIST | W.WS_VSCROLL, ctrlX, y, ctrlW, S(200), id);
                    W.SendMessage(ctrl, W.WM_SETFONT, _font, (IntPtr)1);
                    var choices = f.Choices ?? Array.Empty<string>();
                    foreach (var c in choices) W.SendMessage(ctrl, W.CB_ADDSTRING, IntPtr.Zero, c);
                    W.SendMessage(ctrl, W.CB_SETCURSEL, (IntPtr)Array.IndexOf(choices, f.Get(_work)), IntPtr.Zero);
                    y += S(28);
                    break;
                default:
                    Child("STATIC", f.Label, W.SS_LEFT, left, y + S(3), ctrlX - left - S(8), S(20), 0);
                    ctrl = Child("EDIT", f.Get(_work), W.WS_TABSTOP | W.WS_BORDER | W.ES_AUTOHSCROLL, ctrlX, y, ctrlW, S(22), id);
                    y += S(28);
                    break;
            }
            _controls.Add((f, ctrl));
            id++;
        }
        y += S(8);
        Child("STATIC", "Changes apply at once. File: " + Paths.Settings, W.SS_LEFT, left, y, width - 2 * left, S(36), 0);
        y += S(40);
        _contentHeight = y;
        UpdateScrollBar();
        _loading = false;
    }

    private IntPtr Child(string cls, string? text, uint style, int x, int y, int w, int h, int id)
    {
        var hwnd = W.CreateWindowEx(0, cls, text, W.WS_CHILD | W.WS_VISIBLE | style, x, y - _scroll, w, h, Handle, (IntPtr)id, Instance, IntPtr.Zero);
        W.SendMessage(hwnd, W.WM_SETFONT, _font, (IntPtr)1);
        return hwnd;
    }

    private void UpdateScrollBar()
    {
        W.GetClientRect(Handle, out var rc);
        var si = new SCROLLINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<SCROLLINFO>(),
            fMask = W.SIF_RANGE | W.SIF_PAGE | W.SIF_POS,
            nMin = 0,
            nMax = _contentHeight,
            nPage = (uint)Math.Max(1, rc.Height),
            nPos = _scroll,
        };
        W.SetScrollInfo(Handle, W.SB_VERT, ref si, true);
    }

    private void ScrollTo(int pos)
    {
        W.GetClientRect(Handle, out var rc);
        pos = Math.Clamp(pos, 0, Math.Max(0, _contentHeight - rc.Height));
        int dy = _scroll - pos;
        if (dy == 0) return;
        _scroll = pos;
        W.ScrollWindowEx(Handle, 0, dy, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, W.SW_SCROLLCHILDREN | W.SW_INVALIDATE | W.SW_ERASE);
        UpdateScrollBar();
    }

    private void OnChange(int index, IntPtr hwnd)
    {
        if (_loading || index < 0 || index >= _controls.Count) return;
        var f = _controls[index].Field;
        if (f.IsSpecial)
        {
            ChangeAutostart(W.SendMessage(hwnd, W.BM_GETCHECK, IntPtr.Zero, IntPtr.Zero) == (IntPtr)W.BST_CHECKED);
            return;
        }
        string value = f.Kind switch
        {
            FieldKind.Bool => W.SendMessage(hwnd, W.BM_GETCHECK, IntPtr.Zero, IntPtr.Zero) == (IntPtr)W.BST_CHECKED ? "1" : "0",
            FieldKind.Choice => (f.Choices ?? Array.Empty<string>()).ElementAtOrDefault((int)W.SendMessage(hwnd, W.CB_GETCURSEL, IntPtr.Zero, IntPtr.Zero)) ?? "",
            _ => GetText(hwnd),
        };
        if (f.Set(_work, value)) _apply(_work); // invalid text: no change until it is valid
    }

    private static string GetText(IntPtr hwnd)
    {
        int len = W.GetWindowTextLength(hwnd);
        var buf = new char[len + 1];
        int n = W.GetWindowText(hwnd, buf, buf.Length);
        return new string(buf, 0, n);
    }

    // ───────── Start at logon ─────────

    private void QueryAutostart()
    {
        if (_autostartBox == IntPtr.Zero) return;
        W.EnableWindow(_autostartBox, false);
        var hwnd = Handle;
        Task.Run(() =>
        {
            bool on = Autostart.IsEnabled();
            W.PostMessage(hwnd, WM_AUTOSTART_DONE, (IntPtr)(on ? 1 : 0), IntPtr.Zero);
        });
    }

    private void ChangeAutostart(bool enable)
    {
        W.EnableWindow(_autostartBox, false);
        var hwnd = Handle;
        Task.Run(() =>
        {
            Autostart.Set(enable);
            bool on = Autostart.IsEnabled();
            W.PostMessage(hwnd, WM_AUTOSTART_DONE, (IntPtr)(on ? 1 : 0), IntPtr.Zero);
        });
    }

    protected override IntPtr OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case W.WM_COMMAND:
            {
                int id = W.LoWord(wParam) & 0xFFFF;
                int code = (int)(((long)wParam >> 16) & 0xFFFF);
                if (id >= IdBase && (code == W.BN_CLICKED || code == W.EN_CHANGE || code == W.CBN_SELCHANGE))
                    OnChange(id - IdBase, lParam);
                return IntPtr.Zero;
            }
            case WM_AUTOSTART_DONE:
                _loading = true;
                W.SendMessage(_autostartBox, W.BM_SETCHECK, wParam, IntPtr.Zero);
                _loading = false;
                W.EnableWindow(_autostartBox, true);
                return IntPtr.Zero;
            case W.WM_VSCROLL:
            {
                int req = W.LoWord(wParam) & 0xFFFF;
                W.GetClientRect(Handle, out var rc);
                var si = new SCROLLINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<SCROLLINFO>(), fMask = W.SIF_ALL };
                W.GetScrollInfo(Handle, W.SB_VERT, ref si);
                int pos = req switch
                {
                    W.SB_LINEUP => _scroll - S(24),
                    W.SB_LINEDOWN => _scroll + S(24),
                    W.SB_PAGEUP => _scroll - rc.Height,
                    W.SB_PAGEDOWN => _scroll + rc.Height,
                    W.SB_THUMBTRACK or W.SB_THUMBPOSITION => si.nTrackPos,
                    _ => _scroll,
                };
                ScrollTo(pos);
                return IntPtr.Zero;
            }
            case W.WM_MOUSEWHEEL:
                ScrollTo(_scroll - W.HiWord(wParam) / 120 * S(48));
                return IntPtr.Zero;
            case W.WM_SIZE:
                UpdateScrollBar();
                ScrollTo(_scroll);
                return IntPtr.Zero;
            case W.WM_DESTROY:
                if (_font != IntPtr.Zero) W.DeleteObject(_font);
                if (_boldFont != IntPtr.Zero) W.DeleteObject(_boldFont);
                Closed?.Invoke();
                return IntPtr.Zero;
        }
        return base.OnMessage(msg, wParam, lParam);
    }
}
