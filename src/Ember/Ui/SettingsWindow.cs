using System.Runtime.InteropServices;
using Ember.App;
using Ember.Core.Config;
using Ember.Core.Render;
using Ember.Native;

namespace Ember.Ui;

/// <summary>
/// Settings window, generated from <see cref="SettingsSchema"/>: a page list on the left
/// (General, Layout, Appearance, …) and the rows of the selected page on the right (scrolls).
/// Created when opened and destroyed when closed. Every valid change applies at once and is saved.
/// </summary>
internal sealed class SettingsWindow : Win32Window
{
    private const string ClassName = "Ember.Settings";
    private const int IdBase = 1000, IdNav = 900, IdLayoutList = 901, IdLayoutBase = 910;
    private const uint WM_AUTOSTART_DONE = W.WM_APP + 20;

    private sealed class Row
    {
        public required SettingField Field;
        public IntPtr Ctrl;            // main control (checkbox, edit, combo, trackbar, button)
        public IntPtr Extra;           // slider value text or colour "Pick…" button
        public readonly List<(IntPtr Hwnd, int BaseY)> Hwnds = new();
        public int Page;
    }

    private EmberSettings _work;
    private readonly Action<EmberSettings> _apply;
    private readonly List<Row> _rows = new();
    private readonly List<(IntPtr Hwnd, int BaseY, int Page)> _statics = new();
    private readonly Dictionary<IntPtr, IntPtr> _colorBrushes = new();
    private IntPtr _font, _boldFont, _nav, _layoutList, _layoutText, _spacerPreset;
    private readonly List<(IntPtr Hwnd, int BaseY)> _layoutCtrls = new();
    private bool _loading;
    private int _page, _scroll, _contentHeight;
    private float _scale = 1;
    private IntPtr _autostartBox;
    private int _navWidth;

    public event Action? Closed;

    public SettingsWindow(EmberSettings current, Action<EmberSettings> apply)
    {
        _work = SettingsStore.Clone(current);
        _apply = apply;
    }

    public void Create()
    {
        var icc = new INITCOMMONCONTROLSEX { dwSize = (uint)Marshal.SizeOf<INITCOMMONCONTROLSEX>(), dwICC = W.ICC_BAR_CLASSES | W.ICC_STANDARD_CLASSES };
        W.InitCommonControlsEx(ref icc);
        RegisterClass(ClassName, IntPtr.Zero, W.GetSysColorBrush(W.COLOR_BTNFACE));
        var mon = Monitors.Get(0);
        _scale = Monitors.Dpi(mon.Handle) / 96f;
        int w = S(760), h = Math.Min(S(680), mon.Work.Height - S(40));
        int x = mon.Work.Left + (mon.Work.Width - w) / 2, y = mon.Work.Top + (mon.Work.Height - h) / 2;
        CreateHandle(ClassName, "Ember settings", W.WS_EX_CONTROLPARENT,
            W.WS_OVERLAPPED | W.WS_CAPTION | W.WS_SYSMENU | W.WS_THICKFRAME | W.WS_MINIMIZEBOX | W.WS_VSCROLL | W.WS_CLIPCHILDREN,
            x, y, w, h, IntPtr.Zero);
        _scale = W.GetDpiForWindow(Handle) / 96f;
        _font = W.CreateFont(-S(12), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        _boldFont = W.CreateFont(-S(13), 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        BuildControls();
        ShowPage(0);
        W.ShowWindow(Handle, W.SW_SHOW);
        W.SetForegroundWindow(Handle);
        QueryAutostart();
    }

    private int S(double v) => (int)Math.Round(v * _scale);

    // ───────── Building ─────────

    private void BuildControls()
    {
        _loading = true;
        W.GetClientRect(Handle, out var rc);
        _navWidth = S(150);
        _nav = W.CreateWindowEx(0, "LISTBOX", null, W.WS_CHILD | W.WS_VISIBLE | W.WS_TABSTOP | W.LBS_NOTIFY | W.LBS_NOINTEGRALHEIGHT,
            S(8), S(8), _navWidth - S(8), rc.Height - S(16), Handle, (IntPtr)IdNav, Instance, IntPtr.Zero);
        W.SendMessage(_nav, W.WM_SETFONT, _boldFont, (IntPtr)1);
        foreach (var p in SettingsSchema.Pages) W.SendMessage(_nav, W.LB_ADDSTRING, IntPtr.Zero, p);

        int left = _navWidth + S(16), width = rc.Width - left - S(16);
        int ctrlX = left + S(250), ctrlW = width - S(250);
        int id = IdBase;
        for (int page = 0; page < SettingsSchema.Pages.Length; page++)
        {
            int y = S(10);
            string section = "";
            foreach (var f in SettingsSchema.OnPage(SettingsSchema.Pages[page]))
            {
                if (f.Section != section && f.Section.Length > 0)
                {
                    section = f.Section;
                    y += S(6);
                    var hdr = Static(section, left, y, width, S(20), page, bold: true);
                    _ = hdr;
                    y += S(24);
                }
                var row = new Row { Field = f, Page = page };
                switch (f.Kind)
                {
                    case FieldKind.Bool:
                        row.Ctrl = Child(row, "BUTTON", f.Label, W.BS_AUTOCHECKBOX | W.WS_TABSTOP, left, y, width, S(22), id);
                        if (f.IsSpecial) _autostartBox = row.Ctrl;
                        y += S(26);
                        break;
                    case FieldKind.List:
                        Static(f.Label, left, y, width, S(20), page);
                        y += S(22);
                        row.Ctrl = Child(row, "EDIT", "", W.WS_TABSTOP | W.WS_BORDER | W.ES_MULTILINE | W.ES_AUTOVSCROLL | W.ES_WANTRETURN | W.WS_VSCROLL,
                            left, y, width, S(150), id);
                        y += S(158);
                        break;
                    case FieldKind.Choice:
                        Static(f.Label, left, y + S(3), S(240), S(20), page);
                        row.Ctrl = Child(row, "COMBOBOX", null, W.WS_TABSTOP | W.CBS_DROPDOWNLIST | W.WS_VSCROLL, ctrlX, y, Math.Min(ctrlW, S(220)), S(240), id);
                        foreach (var c in f.Choices ?? Array.Empty<string>())
                            W.SendMessage(row.Ctrl, W.CB_ADDSTRING, IntPtr.Zero, c.Length == 0 ? "(choose…)" : c);
                        y += S(30);
                        break;
                    case FieldKind.Slider:
                    {
                        Static(f.Label, left, y + S(3), S(240), S(20), page);
                        row.Ctrl = Child(row, "msctls_trackbar32", null, W.WS_TABSTOP | W.TBS_NOTICKS, ctrlX, y, ctrlW - S(60), S(26), id);
                        int steps = (int)Math.Round((f.Max - f.Min) / f.Step);
                        W.SendMessage(row.Ctrl, W.TBM_SETRANGE, (IntPtr)1, W.MakeLParam(0, steps));
                        row.Extra = Child(row, "STATIC", "", W.SS_LEFT, ctrlX + ctrlW - S(54), y + S(4), S(54), S(20), 0);
                        y += S(30);
                        break;
                    }
                    case FieldKind.Color:
                        Static(f.Label, left, y + S(3), S(240), S(20), page);
                        row.Ctrl = Child(row, "EDIT", "", W.WS_TABSTOP | W.WS_BORDER | W.ES_AUTOHSCROLL, ctrlX, y, S(100), S(24), id);
                        row.Extra = Child(row, "BUTTON", "Pick…", W.BS_PUSHBUTTON | W.WS_TABSTOP, ctrlX + S(108), y, S(70), S(24), id + 5000);
                        y += S(30);
                        break;
                    case FieldKind.Action:
                        row.Ctrl = Child(row, "BUTTON", f.Label, W.BS_PUSHBUTTON | W.WS_TABSTOP, left, y, S(200), S(28), id);
                        y += S(34);
                        break;
                    case FieldKind.LayoutEditor:
                        row.Ctrl = IntPtr.Zero;
                        y = BuildLayoutEditor(row, left, y, width, page);
                        break;
                    default:
                        Static(f.Label, left, y + S(3), S(240), S(20), page);
                        row.Ctrl = Child(row, "EDIT", "", W.WS_TABSTOP | W.WS_BORDER | W.ES_AUTOHSCROLL, ctrlX, y, ctrlW, S(24), id);
                        y += S(30);
                        break;
                }
                _rows.Add(row);
                id++;
            }
            if (page == 0)
            {
                y += S(10);
                Static("Changes apply at once.", left, y, width, S(36), page);
                y += S(40);
            }
            _pageHeights[page] = y + S(10);
        }
        LoadValues();
        _loading = false;
    }

    private readonly int[] _pageHeights = new int[SettingsSchema.Pages.Length];

    private IntPtr Child(Row row, string cls, string? text, uint style, int x, int y, int w, int h, int id)
    {
        var hwnd = W.CreateWindowEx(0, cls, text, W.WS_CHILD | style, x, y, w, h, Handle, (IntPtr)id, Instance, IntPtr.Zero);
        W.SendMessage(hwnd, W.WM_SETFONT, _font, (IntPtr)1);
        row.Hwnds.Add((hwnd, y));
        return hwnd;
    }

    private IntPtr Static(string text, int x, int y, int w, int h, int page, bool bold = false)
    {
        var hwnd = W.CreateWindowEx(0, "STATIC", text, W.WS_CHILD | W.SS_LEFT, x, y, w, h, Handle, IntPtr.Zero, Instance, IntPtr.Zero);
        W.SendMessage(hwnd, W.WM_SETFONT, bold ? _boldFont : _font, (IntPtr)1);
        _statics.Add((hwnd, y, page));
        return hwnd;
    }

    /// <summary>Layout editor: list of lines (order, on/off) with buttons. Items cannot be removed, only switched off.</summary>
    private int BuildLayoutEditor(Row row, int left, int y, int width, int page)
    {
        int listH = S(300), bw = S(120);
        _layoutList = W.CreateWindowEx(0, "LISTBOX", null, W.WS_CHILD | W.WS_TABSTOP | W.WS_BORDER | W.WS_VSCROLL | W.LBS_NOTIFY | W.LBS_NOINTEGRALHEIGHT,
            left, y, width - bw - S(8), listH, Handle, (IntPtr)IdLayoutList, Instance, IntPtr.Zero);
        W.SendMessage(_layoutList, W.WM_SETFONT, _font, (IntPtr)1);
        row.Hwnds.Add((_layoutList, y));
        _layoutCtrls.Add((_layoutList, y));
        string[] buttons = { "On / off", "Move up", "Move down", "Add header", "Add spacer", "Set text", "Remove" };
        int by = y;
        for (int i = 0; i < buttons.Length; i++)
        {
            var b = W.CreateWindowEx(0, "BUTTON", buttons[i], W.WS_CHILD | W.WS_TABSTOP | W.BS_PUSHBUTTON,
                left + width - bw, by, bw, S(28), Handle, (IntPtr)(IdLayoutBase + i), Instance, IntPtr.Zero);
            W.SendMessage(b, W.WM_SETFONT, _font, (IntPtr)1);
            row.Hwnds.Add((b, by));
            by += S(32);
        }
        y += listH + S(8);
        var label = W.CreateWindowEx(0, "STATIC", "Text / spacer:", W.WS_CHILD | W.SS_LEFT, left, y + S(4), S(100), S(20), Handle, IntPtr.Zero, Instance, IntPtr.Zero);
        W.SendMessage(label, W.WM_SETFONT, _font, (IntPtr)1);
        row.Hwnds.Add((label, y + S(4)));
        _layoutText = W.CreateWindowEx(0, "EDIT", "", W.WS_CHILD | W.WS_TABSTOP | W.WS_BORDER | W.ES_AUTOHSCROLL,
            left + S(104), y, width - bw - S(112) - S(130), S(24), Handle, IntPtr.Zero, Instance, IntPtr.Zero);
        W.SendMessage(_layoutText, W.WM_SETFONT, _font, (IntPtr)1);
        row.Hwnds.Add((_layoutText, y));
        _spacerPreset = W.CreateWindowEx(0, "COMBOBOX", null, W.WS_CHILD | W.WS_TABSTOP | W.CBS_DROPDOWNLIST | W.WS_VSCROLL,
            left + width - bw - S(130), y, S(122), S(200), Handle, IntPtr.Zero, Instance, IntPtr.Zero);
        W.SendMessage(_spacerPreset, W.WM_SETFONT, _font, (IntPtr)1);
        foreach (var p in Layout.SpacerPresets) W.SendMessage(_spacerPreset, W.CB_ADDSTRING, IntPtr.Zero, p);
        W.SendMessage(_spacerPreset, W.CB_SETCURSEL, IntPtr.Zero, IntPtr.Zero);
        row.Hwnds.Add((_spacerPreset, y));
        y += S(30);
        var hint = W.CreateWindowEx(0, "STATIC", "Double-click switches a line on or off. Headers and spacers hide while the lines below them are empty.",
            W.WS_CHILD | W.SS_LEFT, left, y, width, S(36), Handle, IntPtr.Zero, Instance, IntPtr.Zero);
        W.SendMessage(hint, W.WM_SETFONT, _font, (IntPtr)1);
        row.Hwnds.Add((hint, y));
        return y + S(42);
    }

    // ───────── Values ─────────

    /// <summary>Shows new settings (after a preset or a change from elsewhere).</summary>
    public void Reload(EmberSettings s)
    {
        if (!IsAlive) return;
        _work = SettingsStore.Clone(s);
        _loading = true;
        LoadValues();
        _loading = false;
    }

    private void LoadValues()
    {
        foreach (var r in _rows)
        {
            var f = r.Field;
            switch (f.Kind)
            {
                case FieldKind.Bool:
                    if (!f.IsSpecial) W.SendMessage(r.Ctrl, W.BM_SETCHECK, (IntPtr)(f.Get(_work) == "1" ? 1 : 0), IntPtr.Zero);
                    break;
                case FieldKind.Choice:
                    W.SendMessage(r.Ctrl, W.CB_SETCURSEL, (IntPtr)Array.IndexOf(f.Choices ?? Array.Empty<string>(), f.Get(_work)), IntPtr.Zero);
                    break;
                case FieldKind.Slider:
                {
                    SettingsSchema.TryNum(f.Get(_work), f.Min, f.Max, out double v);
                    W.SendMessage(r.Ctrl, W.TBM_SETPOS, (IntPtr)1, (IntPtr)(int)Math.Round((v - f.Min) / f.Step));
                    W.SetWindowText(r.Extra, SettingsSchema.Num(v));
                    break;
                }
                case FieldKind.Action:
                    break;
                case FieldKind.LayoutEditor:
                    FillLayoutList(0);
                    break;
                default:
                    W.SetWindowText(r.Ctrl, f.Get(_work));
                    break;
            }
        }
        W.InvalidateRect(Handle, IntPtr.Zero, true);
    }

    private void FillLayoutList(int select)
    {
        if (_layoutList == IntPtr.Zero) return;
        int top = (int)W.SendMessage(_layoutList, W.LB_GETTOPINDEX, IntPtr.Zero, IntPtr.Zero);
        W.SendMessage(_layoutList, W.LB_RESETCONTENT, IntPtr.Zero, IntPtr.Zero);
        foreach (var e in _work.Entries)
        {
            string mark = e.Enabled ? "[x] " : "[  ] ";
            string indent = e.Kind == "item" ? "" : "      ";
            W.SendMessage(_layoutList, W.LB_ADDSTRING, IntPtr.Zero, mark + indent + e.Describe());
        }
        W.SendMessage(_layoutList, W.LB_SETTOPINDEX, (IntPtr)Math.Max(0, top), IntPtr.Zero);
        W.SendMessage(_layoutList, W.LB_SETCURSEL, (IntPtr)Math.Clamp(select, -1, _work.Entries.Count - 1), IntPtr.Zero);
    }

    private void OnLayoutButton(int index)
    {
        int sel = (int)W.SendMessage(_layoutList, W.LB_GETCURSEL, IntPtr.Zero, IntPtr.Zero);
        var list = _work.Entries;
        bool valid = sel >= 0 && sel < list.Count;
        string text = GetText(_layoutText).Trim();
        switch (index)
        {
            case 0 when valid: list[sel].Enabled = !list[sel].Enabled; break;
            case 1 when valid && sel > 0: (list[sel - 1], list[sel]) = (list[sel], list[sel - 1]); sel--; break;
            case 2 when valid && sel < list.Count - 1: (list[sel + 1], list[sel]) = (list[sel], list[sel + 1]); sel++; break;
            case 3:
                list.Insert(valid ? sel : list.Count, LayoutEntry.Header(text.Length > 0 ? text : "section"));
                if (!valid) sel = list.Count - 1;
                break;
            case 4:
            {
                int p = (int)W.SendMessage(_spacerPreset, W.CB_GETCURSEL, IntPtr.Zero, IntPtr.Zero);
                string preset = p >= 0 && p < Layout.SpacerPresets.Length ? Layout.SpacerPresets[p] : "blank";
                if (text.Length > 0 && preset == "blank") preset = "custom";
                list.Insert(valid ? sel : list.Count, LayoutEntry.Spacer(preset, preset == "custom" ? text : null, collapse: false));
                if (!valid) sel = list.Count - 1;
                break;
            }
            case 5 when valid && list[sel].Kind != "item":
                list[sel].Text = text;
                if (list[sel].Kind == "spacer") list[sel].Preset = text.Length > 0 ? "custom" : list[sel].Preset;
                break;
            case 6 when valid && list[sel].Kind != "item":
                list.RemoveAt(sel);
                sel = Math.Min(sel, list.Count - 1);
                break;
            default:
                return;
        }
        FillLayoutList(sel);
        _apply(_work);
    }

    private void OnChange(Row row, int notify)
    {
        if (_loading) return;
        var f = row.Field;
        if (f.IsSpecial)
        {
            ChangeAutostart(W.SendMessage(row.Ctrl, W.BM_GETCHECK, IntPtr.Zero, IntPtr.Zero) == (IntPtr)W.BST_CHECKED);
            return;
        }
        string value;
        switch (f.Kind)
        {
            case FieldKind.Bool:
                value = W.SendMessage(row.Ctrl, W.BM_GETCHECK, IntPtr.Zero, IntPtr.Zero) == (IntPtr)W.BST_CHECKED ? "1" : "0";
                break;
            case FieldKind.Choice:
                value = (f.Choices ?? Array.Empty<string>()).ElementAtOrDefault((int)W.SendMessage(row.Ctrl, W.CB_GETCURSEL, IntPtr.Zero, IntPtr.Zero)) ?? "";
                break;
            case FieldKind.Slider:
            {
                int pos = (int)W.SendMessage(row.Ctrl, W.TBM_GETPOS, IntPtr.Zero, IntPtr.Zero);
                double v = f.Min + pos * f.Step;
                W.SetWindowText(row.Extra, SettingsSchema.Num(v));
                value = SettingsSchema.Num(v);
                break;
            }
            case FieldKind.Action:
                RunAction(f.Action ?? "");
                return;
            default:
                value = GetText(row.Ctrl);
                break;
        }
        if (!f.Set(_work, value)) return; // invalid text: no change until it is valid
        _apply(_work);
        if (f.ReloadsOthers) Reload(_work);
        if (f.Kind == FieldKind.Color) W.InvalidateRect(row.Ctrl, IntPtr.Zero, true);
    }

    private void PickColor(Row row)
    {
        var current = Rgb.Parse(row.Field.Get(_work), Palette.Bright);
        if (Dialogs.PickColor(Handle, current) is not { } c) return;
        W.SetWindowText(row.Ctrl, c.ToHex()); // EN_CHANGE applies it
    }

    private void RunAction(string action)
    {
        try
        {
            switch (action)
            {
                case "app.openlog": Paths.OpenLog(); break;
                case "app.opensettings": Paths.Open(Paths.Settings); break;
                case "app.openfolder": Paths.Open(Paths.AppData); break;
                case "theme.export":
                    if (Dialogs.SaveJson(Handle, "Export theme", "ember-theme-" + _work.Theme.Name + ".json") is { } tp)
                        File.WriteAllText(tp, ThemeStore.Serialize(_work.Theme));
                    break;
                case "theme.import":
                    if (Dialogs.OpenJson(Handle, "Import theme") is { } ti)
                    {
                        if (ThemeStore.Parse(File.ReadAllText(ti)) is { } t) { _work.Theme = t; _apply(_work); Reload(_work); }
                        else Error("This file is not an Ember theme.");
                    }
                    break;
                case "layout.export":
                    if (Dialogs.SaveJson(Handle, "Export layout", "ember-layout.json") is { } lp)
                        File.WriteAllText(lp, new LayoutPreset { Name = Path.GetFileNameWithoutExtension(lp), Entries = _work.Entries }.ToJson());
                    break;
                case "layout.import":
                    if (Dialogs.OpenJson(Handle, "Import layout") is { } li)
                    {
                        if (LayoutPreset.Parse(File.ReadAllText(li)) is { } p) { _work.Entries = p.Entries; _apply(_work); Reload(_work); }
                        else Error("This file is not an Ember layout.");
                    }
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error(ex.Message);
        }
    }

    private void Error(string text) => W.MessageBox(Handle, text, "Ember", W.MB_ICONWARNING);

    private static string GetText(IntPtr hwnd)
    {
        int len = W.GetWindowTextLength(hwnd);
        var buf = new char[len + 1];
        int n = W.GetWindowText(hwnd, buf, buf.Length);
        return new string(buf, 0, n);
    }

    // ───────── Pages and scrolling ─────────

    private void ShowPage(int page)
    {
        _page = Math.Clamp(page, 0, SettingsSchema.Pages.Length - 1);
        _scroll = 0;
        _contentHeight = _pageHeights[_page];
        W.SendMessage(_nav, W.LB_SETCURSEL, (IntPtr)_page, IntPtr.Zero);
        foreach (var r in _rows)
            foreach (var (h, _) in r.Hwnds) W.ShowWindow(h, r.Page == _page ? W.SW_SHOWNOACTIVATE : W.SW_HIDE);
        foreach (var (h, _, p) in _statics) W.ShowWindow(h, p == _page ? W.SW_SHOWNOACTIVATE : W.SW_HIDE);
        Reposition();
        UpdateScrollBar();
        W.InvalidateRect(Handle, IntPtr.Zero, true);
    }

    /// <summary>
    /// Moves the controls of the page to their base position minus the scroll offset. Drawing is off
    /// while they move, then the window and all controls are drawn once (no tearing while scrolling).
    /// </summary>
    private void Reposition()
    {
        void Move(IntPtr h, int baseY)
        {
            if (!W.GetWindowRect(h, out var r)) return;
            var p = new POINT(r.Left, r.Top);
            W.ScreenToClient(Handle, ref p);
            if (p.Y == baseY - _scroll) return;
            W.SetWindowPos(h, IntPtr.Zero, p.X, baseY - _scroll, 0, 0,
                W.SWP_NOSIZE | W.SWP_NOZORDER | W.SWP_NOACTIVATE | W.SWP_NOREDRAW | W.SWP_NOCOPYBITS);
        }
        W.SendMessage(Handle, W.WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        try
        {
            foreach (var r in _rows.Where(r => r.Page == _page))
                foreach (var (h, y) in r.Hwnds) Move(h, y);
            foreach (var (h, y, p) in _statics.Where(s => s.Page == _page)) Move(h, y);
        }
        finally
        {
            W.SendMessage(Handle, W.WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            W.RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, W.RDW_INVALIDATE | W.RDW_ERASE | W.RDW_ALLCHILDREN | W.RDW_UPDATENOW);
        }
    }

    private void UpdateScrollBar()
    {
        W.GetClientRect(Handle, out var rc);
        var si = new SCROLLINFO
        {
            cbSize = (uint)Marshal.SizeOf<SCROLLINFO>(),
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
        if (pos == _scroll) return;
        _scroll = pos;
        Reposition();
        UpdateScrollBar();
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

    // ───────── Messages ─────────

    private Row? RowOf(IntPtr hwnd) => _rows.FirstOrDefault(r => r.Ctrl == hwnd);

    protected override IntPtr OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case W.WM_COMMAND:
            {
                int id = W.LoWord(wParam) & 0xFFFF;
                int code = (int)(((long)wParam >> 16) & 0xFFFF);
                if (id == IdNav && code == W.LBN_SELCHANGE)
                {
                    ShowPage((int)W.SendMessage(_nav, W.LB_GETCURSEL, IntPtr.Zero, IntPtr.Zero));
                    return IntPtr.Zero;
                }
                if (id == IdLayoutList && code == W.LBN_DBLCLK)
                {
                    OnLayoutButton(0);
                    return IntPtr.Zero;
                }
                if (id >= IdLayoutBase && id < IdLayoutBase + 10 && code == W.BN_CLICKED)
                {
                    OnLayoutButton(id - IdLayoutBase);
                    return IntPtr.Zero;
                }
                if (id >= IdBase + 5000 && code == W.BN_CLICKED)
                {
                    var row = _rows.FirstOrDefault(r => r.Extra == lParam);
                    if (row != null) PickColor(row);
                    return IntPtr.Zero;
                }
                if (id >= IdBase && (code == W.BN_CLICKED || code == W.EN_CHANGE || code == W.CBN_SELCHANGE) && RowOf(lParam) is { } r2)
                    OnChange(r2, code);
                return IntPtr.Zero;
            }
            case W.WM_HSCROLL:
                if (RowOf(lParam) is { Field.Kind: FieldKind.Slider } sr) OnChange(sr, 0);
                return IntPtr.Zero;
            case W.WM_CTLCOLOREDIT:
            {
                // Colour fields show their colour.
                var row = RowOf(lParam);
                if (row?.Field.Kind == FieldKind.Color && Rgb.TryParse(row.Field.Get(_work), out var c))
                {
                    if (_colorBrushes.TryGetValue(lParam, out var old)) W.DeleteObject(old);
                    var brush = W.CreateSolidBrush(c.ToColorRef());
                    _colorBrushes[lParam] = brush;
                    W.SetBkColor(wParam, c.ToColorRef());
                    double lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
                    W.SetTextColor(wParam, lum > 140 ? 0x000000u : 0xFFFFFFu);
                    return brush;
                }
                break;
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
                var si = new SCROLLINFO { cbSize = (uint)Marshal.SizeOf<SCROLLINFO>(), fMask = W.SIF_ALL };
                W.GetScrollInfo(Handle, W.SB_VERT, ref si);
                int pos = req switch
                {
                    W.SB_LINEUP => _scroll - S(30),
                    W.SB_LINEDOWN => _scroll + S(30),
                    W.SB_PAGEUP => _scroll - rc.Height,
                    W.SB_PAGEDOWN => _scroll + rc.Height,
                    W.SB_THUMBTRACK or W.SB_THUMBPOSITION => si.nTrackPos,
                    _ => _scroll,
                };
                ScrollTo(pos);
                return IntPtr.Zero;
            }
            case W.WM_MOUSEWHEEL:
                ScrollTo(_scroll - W.HiWord(wParam) * S(60) / 120);
                return IntPtr.Zero;
            case W.WM_SIZE:
                if (_nav != IntPtr.Zero)
                {
                    W.GetClientRect(Handle, out var cr);
                    W.MoveWindow(_nav, S(8), S(8), _navWidth - S(8), cr.Height - S(16), true);
                }
                UpdateScrollBar();
                ScrollTo(_scroll);
                return IntPtr.Zero;
            case W.WM_DESTROY:
                foreach (var b in _colorBrushes.Values) W.DeleteObject(b);
                if (_font != IntPtr.Zero) W.DeleteObject(_font);
                if (_boldFont != IntPtr.Zero) W.DeleteObject(_boldFont);
                Closed?.Invoke();
                return IntPtr.Zero;
        }
        return base.OnMessage(msg, wParam, lParam);
    }
}
