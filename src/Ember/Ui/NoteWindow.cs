using System.Runtime.InteropServices;
using Ember.App;
using Ember.Core.Config;
using Ember.Core.Notes;
using Ember.Core.Render;
using Ember.Native;

namespace Ember.Ui;

/// <summary>
/// One sticky note: a borderless window on the desktop layer with a title bar (drag to move,
/// slightly lighter than the note), minimize and close buttons (with instant tooltips), and a
/// RichEdit editor. Markdown is styled as you type; markers stay visible but dim.
/// Click a checkbox to tick it; Ctrl+click a link to open it. Drag an edge to resize.
/// </summary>
internal sealed class NoteWindow : Win32Window
{
    private const string ClassName = "Ember.Note";
    private const uint SaveTimer = 1, WM_SHOWMENU = W.WM_APP + 1;
    private const int CmdNew = 1, CmdMinimize = 2, CmdDelete = 3;
    private static bool _richEditLoaded;

    private IntPtr _edit, _bgBrush, _textDoc;
    private bool _styling;
    private float _scale = 1;
    private EmberSettings _cfg;
    private ThemeColors _c;
    private int _hover = -1; // 0 = minimize, 1 = close
    private bool _tracking;

    public string Id { get; }
    public string Text { get; private set; }
    public bool Minimized { get; private set; }

    /// <summary>First non-empty line without markdown markers (for the toolbar tooltip).</summary>
    public string Title
    {
        get
        {
            var line = Text.Split('\r', '\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
            line = line.TrimStart('#', '>', '-', '*', '+', ' ').Replace("[ ]", "").Replace("[x]", "").Replace("**", "").Trim();
            return line.Length > 60 ? line[..60] + "…" : line;
        }
    }

    public event Action<NoteWindow>? Changed;      // text changed (save after 1 s)
    public event Action<NoteWindow>? MovedOrSized;
    public event Action<NoteWindow>? DeleteRequested;
    public event Action<NoteWindow>? MinimizeRequested;
    public event Action? NewRequested;

    public NoteWindow(string id, string text, EmberSettings cfg)
    {
        Id = id;
        Text = text;
        _cfg = cfg;
        _c = ThemeColors.From(cfg.Theme);
    }

    private Rgb Background => Rgb.Parse(_cfg.Theme.Background.Color, Palette.Background);
    private Rgb TitleBar => Rgb.Lerp(Background, _c.Text, 0.10);
    private Rgb CodeBackground => Rgb.Lerp(Background, _c.Text, 0.12);

    public void Create(RECT r, bool minimized)
    {
        if (!_richEditLoaded)
        {
            W.LoadLibrary("Msftedit.dll");
            _richEditLoaded = true;
        }
        RegisterClass(ClassName, IntPtr.Zero, IntPtr.Zero);
        CreateHandle(ClassName, "Ember note", W.WS_EX_LAYERED | W.WS_EX_TOOLWINDOW, W.WS_POPUP | W.WS_CLIPCHILDREN,
            r.Left, r.Top, Math.Max(80, r.Width), Math.Max(60, r.Height), WidgetWindow.DesktopOwner());
        _scale = W.GetDpiForWindow(Handle) / 96f;

        _edit = W.CreateWindowEx(0, "RICHEDIT50W", null,
            W.WS_CHILD | W.WS_VISIBLE | W.ES_MULTILINE | W.ES_AUTOVSCROLL | W.ES_WANTRETURN | W.ES_NOHIDESEL,
            0, 0, 10, 10, Handle, (IntPtr)100, Instance, IntPtr.Zero);
        W.SendMessage(_edit, W.EM_EXLIMITTEXT, IntPtr.Zero, (IntPtr)(1 << 20));
        W.SendMessage(_edit, W.WM_SETTEXT, IntPtr.Zero, Text);
        W.SendMessage(_edit, W.EM_SETEVENTMASK, IntPtr.Zero, (IntPtr)(W.ENM_CHANGE | W.ENM_MOUSEEVENTS));
        _textDoc = RichEditOle.TextDocument(_edit);
        ApplySettings(_cfg);
        DoLayout();
        Minimized = minimized;
        if (!minimized)
        {
            W.ShowWindow(Handle, W.SW_SHOWNOACTIVATE);
            W.SetWindowPos(Handle, W.HWND_BOTTOM, 0, 0, 0, 0, W.SWP_NOMOVE | W.SWP_NOSIZE | W.SWP_NOACTIVATE);
        }
    }

    public void Focus()
    {
        W.SetForegroundWindow(Handle);
        W.SetFocus(_edit);
    }

    /// <summary>Hides the note (it shows as an icon in the widget toolbar) or shows it again.</summary>
    public void SetMinimized(bool minimized)
    {
        Minimized = minimized;
        Tooltip.HideShared();
        if (minimized) W.ShowWindow(Handle, W.SW_HIDE);
        else
        {
            W.ShowWindow(Handle, W.SW_SHOW);
            Focus();
        }
    }

    public void ApplySettings(EmberSettings cfg)
    {
        _cfg = cfg;
        _c = ThemeColors.From(cfg.Theme);
        // The note keeps a readable background: at least 75 % opaque.
        int opacity = Math.Clamp(Math.Max(cfg.Theme.Background.Opacity + 35, 75), 0, 100);
        W.SetLayeredWindowAttributes(Handle, 0, (byte)Math.Round(255 * opacity / 100.0), W.LWA_ALPHA);
        if (_bgBrush != IntPtr.Zero) W.DeleteObject(_bgBrush);
        _bgBrush = W.CreateSolidBrush(Background.ToColorRef());
        W.SendMessage(_edit, W.EM_SETBKGNDCOLOR, IntPtr.Zero, (IntPtr)Background.ToColorRef());
        Restyle();
        W.InvalidateRect(Handle, IntPtr.Zero, true);
    }

    public void Reattach()
    {
        if (!IsAlive) return;
        W.SetWindowLongPtr(Handle, W.GWLP_HWNDPARENT, WidgetWindow.DesktopOwner());
        if (!Minimized) W.ShowWindow(Handle, W.SW_SHOWNOACTIVATE);
    }

    public void BringToFront()
    {
        if (IsAlive && !Minimized) W.SetWindowPos(Handle, W.HWND_TOP, 0, 0, 0, 0, W.SWP_NOMOVE | W.SWP_NOSIZE | W.SWP_NOACTIVATE);
    }

    public RECT Bounds => W.GetWindowRect(Handle, out var r) ? r : default;

    private int Margin => (int)(6 * _scale);
    private int TitleHeight => (int)(26 * _scale);
    private int ButtonWidth => (int)(30 * _scale);

    private RECT ButtonRect(int i)
    {
        W.GetClientRect(Handle, out var rc);
        int right = rc.Width - i * ButtonWidth; // 1 = close (rightmost), 0 = minimize
        return i == 1 ? new RECT(rc.Width - ButtonWidth, 0, rc.Width, TitleHeight) : new RECT(rc.Width - 2 * ButtonWidth, 0, rc.Width - ButtonWidth, TitleHeight);
    }

    private int ButtonAt(int x, int y)
    {
        for (int i = 0; i < 2; i++) if (ButtonRect(i).Contains(x, y)) return i;
        return -1;
    }

    private void DoLayout()
    {
        W.GetClientRect(Handle, out var rc);
        W.MoveWindow(_edit, Margin + (int)(2 * _scale), TitleHeight + (int)(4 * _scale),
            Math.Max(10, rc.Width - 2 * Margin - (int)(4 * _scale)), Math.Max(10, rc.Height - TitleHeight - Margin - (int)(4 * _scale)), true);
    }

    // ───────── Title bar ─────────

    private void Paint(IntPtr hdc)
    {
        W.GetClientRect(Handle, out var rc);
        Gdip.GdipCreateBitmapFromScan0(rc.Width, rc.Height, 0, Gdip.PixelFormat32bppPARGB, IntPtr.Zero, out var bmp);
        Gdip.GdipGetImageGraphicsContext(bmp, out var g);
        Gdip.GdipGraphicsClear(g, Background.ToArgb());
        Gdip.GdipCreateSolidFill(TitleBar.ToArgb(), out var bar);
        Gdip.GdipFillRectangle(g, bar, 0, 0, rc.Width, TitleHeight);
        Gdip.GdipDeleteBrush(bar);
        Gdip.GdipSetTextRenderingHint(g, 5);

        Gdip.GdipCreateFontFamilyFromName("Segoe UI", IntPtr.Zero, out var fam);
        Gdip.GdipCreateFont(fam, 11.5f * _scale, Gdip.FontStyleRegular, Gdip.UnitPixel, out var font);
        Gdip.GdipCreateSolidFill(_c.Unit.ToArgb(), out var brush);
        var titleRect = new RectF(8 * _scale, 0, rc.Width - 2 * ButtonWidth - 12 * _scale, TitleHeight);
        Gdip.GdipStringFormatGetGenericDefault(out var fmt);
        Gdip.GdipCloneStringFormat(fmt, out var lineFmt);
        Gdip.GdipSetStringFormatLineAlign(lineFmt, Gdip.StringAlignmentCenter);
        Gdip.GdipSetStringFormatFlags(lineFmt, Gdip.StringFormatFlagsNoWrap);
        string title = Title.Length > 0 ? Title : "note";
        Gdip.GdipDrawString(g, title, title.Length, font, ref titleRect, lineFmt, brush);

        // Buttons: minimize and close, icon font glyphs centred.
        IntPtr iconFam = IntPtr.Zero, iconFont = IntPtr.Zero;
        if (Gdip.GdipCreateFontFamilyFromName("Segoe Fluent Icons", IntPtr.Zero, out iconFam) == 0 ||
            Gdip.GdipCreateFontFamilyFromName("Segoe MDL2 Assets", IntPtr.Zero, out iconFam) == 0)
            Gdip.GdipCreateFont(iconFam, 10 * _scale, Gdip.FontStyleRegular, Gdip.UnitPixel, out iconFont);
        Gdip.GdipSetStringFormatAlign(lineFmt, Gdip.StringAlignmentCenter);
        for (int i = 0; i < 2; i++)
        {
            var r = ButtonRect(i);
            if (i == _hover)
            {
                var hover = i == 1 ? _c.Crit.ToArgb(0xC0) : Rgb.Lerp(TitleBar, _c.Text, 0.15).ToArgb();
                Gdip.GdipCreateSolidFill(hover, out var hb);
                Gdip.GdipFillRectangle(g, hb, r.Left, r.Top, r.Width, r.Height);
                Gdip.GdipDeleteBrush(hb);
            }
            Gdip.GdipSetSolidFillColor(brush, (i == _hover ? _c.Text : _c.Icon).ToArgb());
            string glyph = (i == 0 ? Icons.Minimize : Icons.Close).ToString();
            var gr = new RectF(r.Left, r.Top, r.Width, r.Height);
            if (iconFont != IntPtr.Zero) Gdip.GdipDrawString(g, glyph, 1, iconFont, ref gr, lineFmt, brush);
            else Gdip.GdipDrawString(g, i == 0 ? "–" : "×", 1, font, ref gr, lineFmt, brush);
        }
        if (iconFont != IntPtr.Zero) Gdip.GdipDeleteFont(iconFont);
        if (iconFam != IntPtr.Zero) Gdip.GdipDeleteFontFamily(iconFam);
        Gdip.GdipDeleteStringFormat(lineFmt);
        Gdip.GdipDeleteBrush(brush);
        Gdip.GdipDeleteFont(font);
        Gdip.GdipDeleteFontFamily(fam);

        Gdip.GdipCreateFromHDC(hdc, out var screen);
        Gdip.GdipDrawImageI(screen, bmp, 0, 0);
        Gdip.GdipDeleteGraphics(screen);
        Gdip.GdipDeleteGraphics(g);
        Gdip.GdipDisposeImage(bmp);
    }

    private void SetHover(int b)
    {
        if (b == _hover) return;
        _hover = b;
        var r = ButtonRect(0);
        W.InvalidateRect(Handle, IntPtr.Zero, false);
        if (b >= 0 && W.GetCursorPos(out var p))
            Tooltip.Shared.Show(b == 0 ? "Minimize (shows as an icon in the widget toolbar)" : "Delete note (no undo)", p);
        else Tooltip.HideShared();
    }

    // ───────── Styling ─────────

    private string ReadText()
    {
        var lenEx = new GETTEXTLENGTHEX { flags = W.GTL_DEFAULT | W.GTL_NUMCHARS, codepage = 1200 };
        int len = (int)W.SendMessage(_edit, W.EM_GETTEXTLENGTHEX, ref lenEx, IntPtr.Zero);
        if (len <= 0) return "";
        var buf = new char[len + 1];
        var gt = new GETTEXTEX { cb = (uint)(buf.Length * 2), flags = W.GT_DEFAULT, codepage = 1200 };
        int n = (int)W.SendMessage(_edit, W.EM_GETTEXTEX, ref gt, buf);
        return new string(buf, 0, Math.Clamp(n, 0, len));
    }

    private int BaseTwips => (int)Math.Round(_cfg.Layout.FontSize * 20);

    private CHARFORMAT2 Format(MdSpan span)
    {
        var style = span.Style;
        var cf = new CHARFORMAT2
        {
            cbSize = (uint)Marshal.SizeOf<CHARFORMAT2>(),
            dwMask = W.CFM_COLOR | W.CFM_BOLD | W.CFM_ITALIC | W.CFM_STRIKEOUT | W.CFM_UNDERLINE | W.CFM_BACKCOLOR,
            szFaceName = "",
        };
        Rgb color = _c.Text;
        if ((style & MdStyle.Heading) != 0) color = _c.Header;
        if ((style & MdStyle.Quote) != 0) color = _c.Label;
        if ((style & MdStyle.Link) != 0) color = _c.NetDown;
        if ((style & (MdStyle.Marker | MdStyle.Done | MdStyle.Rule)) != 0) color = _c.Dim;
        cf.crTextColor = color.ToColorRef();
        cf.crBackColor = ((style & (MdStyle.Code | MdStyle.CodeBlock)) != 0 ? CodeBackground : Background).ToColorRef();
        if ((style & MdStyle.Bold) != 0) cf.dwEffects |= W.CFE_BOLD;
        if ((style & MdStyle.Italic) != 0 || ((style & MdStyle.Quote) != 0 && (style & MdStyle.Marker) == 0)) cf.dwEffects |= W.CFE_ITALIC;
        if (((style & MdStyle.Done) != 0 && (style & MdStyle.Marker) == 0) || ((style & MdStyle.Strike) != 0 && (style & MdStyle.Marker) == 0))
            cf.dwEffects |= W.CFE_STRIKEOUT;
        if ((style & MdStyle.Link) != 0) cf.dwEffects |= W.CFE_UNDERLINE;
        if ((style & MdStyle.Heading) != 0)
        {
            double factor = span.Level switch { 1 => 1.6, 2 => 1.35, 3 => 1.15, _ => 1.0 };
            cf.dwMask |= W.CFM_SIZE;
            cf.yHeight = (int)Math.Round(BaseTwips * factor);
        }
        return cf;
    }

    /// <summary>Styles all text again. Keeps the selection; no change events while styling.</summary>
    private void Restyle()
    {
        if (_edit == IntPtr.Zero || _styling) return;
        _styling = true;
        RichEditOle.SuspendUndo(_textDoc); // styling must not be in Ctrl+Z
        try
        {
            string text = ReadText();
            Text = text;
            var mask = W.SendMessage(_edit, W.EM_GETEVENTMASK, IntPtr.Zero, IntPtr.Zero);
            W.SendMessage(_edit, W.EM_SETEVENTMASK, IntPtr.Zero, IntPtr.Zero);
            W.SendMessage(_edit, W.WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            var sel = new CHARRANGE();
            W.SendMessage(_edit, W.EM_EXGETSEL, IntPtr.Zero, ref sel);

            var all = Format(new MdSpan(0, 0, MdStyle.Normal));
            all.dwMask |= W.CFM_FACE | W.CFM_SIZE | W.CFM_CHARSET;
            all.szFaceName = string.IsNullOrWhiteSpace(_cfg.Layout.Font) ? "Cascadia Mono" : _cfg.Layout.Font;
            all.yHeight = BaseTwips;
            all.bCharSet = 1; // DEFAULT_CHARSET
            W.SendMessage(_edit, W.EM_SETCHARFORMAT, (IntPtr)W.SCF_ALL, ref all);
            var everything = new CHARRANGE { cpMin = 0, cpMax = -1 };
            W.SendMessage(_edit, W.EM_EXSETSEL, IntPtr.Zero, ref everything);
            SetIndent(0);

            foreach (var span in MarkdownStyler.Style(text))
            {
                var range = new CHARRANGE { cpMin = span.Start, cpMax = span.End };
                W.SendMessage(_edit, W.EM_EXSETSEL, IntPtr.Zero, ref range);
                var cf = Format(span);
                W.SendMessage(_edit, W.EM_SETCHARFORMAT, (IntPtr)W.SCF_SELECTION, ref cf);
                // Quotes: indent the paragraph by its depth.
                if ((span.Style & MdStyle.Quote) != 0 && (span.Style & MdStyle.Marker) == 0) SetIndent(span.Level * 280);
            }

            W.SendMessage(_edit, W.EM_EXSETSEL, IntPtr.Zero, ref sel);
            // Typing continues in the normal style.
            var typing = Format(new MdSpan(0, 0, MdStyle.Normal));
            typing.dwMask |= W.CFM_SIZE;
            typing.yHeight = BaseTwips;
            W.SendMessage(_edit, W.EM_SETCHARFORMAT, (IntPtr)W.SCF_SELECTION, ref typing);
            W.SendMessage(_edit, W.WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            W.InvalidateRect(_edit, IntPtr.Zero, true);
            W.SendMessage(_edit, W.EM_SETEVENTMASK, IntPtr.Zero, mask);
        }
        finally
        {
            RichEditOle.ResumeUndo(_textDoc);
            _styling = false;
        }
        W.InvalidateRect(Handle, IntPtr.Zero, false); // title may have changed
    }

    private void SetIndent(int twips)
    {
        var pf = new PARAFORMAT2 { cbSize = (uint)Marshal.SizeOf<PARAFORMAT2>(), dwMask = W.PFM_STARTINDENT, dxStartIndent = twips };
        W.SendMessage(_edit, W.EM_SETPARAFORMAT, IntPtr.Zero, ref pf);
    }

    // ───────── Mouse: checkboxes, links and menu ─────────

    private bool OnEditMouse(uint msg, IntPtr lParam)
    {
        if (msg == W.WM_LBUTTONDOWN)
        {
            var pt = new POINT(W.LoWord(lParam), W.HiWord(lParam));
            int index = (int)W.SendMessage(_edit, W.EM_CHARFROMPOS, IntPtr.Zero, ref pt);
            string text = ReadText();
            if (W.GetKeyState(W.VK_CONTROL) < 0 && MarkdownStyler.LinkAt(text, index) is { } url &&
                (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                 url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)))
            {
                W.ShellExecute(IntPtr.Zero, "open", url, null, null, W.SW_SHOWNORMAL);
                return true;
            }
            int mark = MarkdownStyler.CheckboxAt(text, index);
            if (mark < 0) return false;
            string replacement = text[mark] is 'x' or 'X' ? " " : "x";
            var range = new CHARRANGE { cpMin = mark, cpMax = mark + 1 };
            W.SendMessage(_edit, W.EM_EXSETSEL, IntPtr.Zero, ref range);
            W.SendMessage(_edit, W.EM_REPLACESEL, (IntPtr)1, replacement);
            return true;
        }
        if (msg == W.WM_RBUTTONUP)
        {
            // Show the menu after RichEdit has returned (the menu can delete this window).
            W.PostMessage(Handle, WM_SHOWMENU, IntPtr.Zero, IntPtr.Zero);
            return true;
        }
        return false;
    }

    private void ShowMenu(POINT sp)
    {
        int cmd = TrayIcon.ShowMenu(Handle, sp, (CmdNew, "New note", false), (CmdMinimize, "Minimize", false), (0, "", false), (CmdDelete, "Delete note", false));
        if (cmd == CmdNew) NewRequested?.Invoke();
        else if (cmd == CmdMinimize) MinimizeRequested?.Invoke(this);
        else if (cmd == CmdDelete) DeleteRequested?.Invoke(this);
    }

    protected override IntPtr OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case W.WM_NCHITTEST:
            {
                var pt = new POINT(W.LoWord(lParam), W.HiWord(lParam));
                W.ScreenToClient(Handle, ref pt);
                W.GetClientRect(Handle, out var rc);
                int e = Margin;
                bool l = pt.X < e, r = pt.X >= rc.Width - e, t = pt.Y < 4, b = pt.Y >= rc.Height - e;
                if (t && l) return (IntPtr)W.HTTOPLEFT;
                if (t && r) return (IntPtr)W.HTTOPRIGHT;
                if (b && l) return (IntPtr)W.HTBOTTOMLEFT;
                if (b && r) return (IntPtr)W.HTBOTTOMRIGHT;
                if (t) return (IntPtr)W.HTTOP;
                if (ButtonAt(pt.X, pt.Y) >= 0) return (IntPtr)W.HTCLIENT;
                if (l) return (IntPtr)W.HTLEFT;
                if (r) return (IntPtr)W.HTRIGHT;
                if (b) return (IntPtr)W.HTBOTTOM;
                if (pt.Y < TitleHeight) return (IntPtr)W.HTCAPTION;
                return (IntPtr)W.HTCLIENT;
            }
            case W.WM_PAINT:
            {
                var hdc = W.BeginPaint(Handle, out var ps);
                Paint(hdc);
                W.EndPaint(Handle, ref ps);
                return IntPtr.Zero;
            }
            case W.WM_ERASEBKGND:
                return (IntPtr)1;
            case W.WM_MOUSEMOVE:
                SetHover(ButtonAt(W.LoWord(lParam), W.HiWord(lParam)));
                if (!_tracking)
                {
                    var tme = new TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(), dwFlags = W.TME_LEAVE, hwndTrack = Handle };
                    _tracking = W.TrackMouseEvent(ref tme);
                }
                return IntPtr.Zero;
            case W.WM_MOUSELEAVE:
                _tracking = false;
                SetHover(-1);
                return IntPtr.Zero;
            case W.WM_LBUTTONUP:
            {
                int btn = ButtonAt(W.LoWord(lParam), W.HiWord(lParam));
                Tooltip.HideShared();
                if (btn == 0) MinimizeRequested?.Invoke(this);
                else if (btn == 1) DeleteRequested?.Invoke(this); // no confirmation
                return IntPtr.Zero;
            }
            case W.WM_NCRBUTTONUP:
            case WM_SHOWMENU:
                if (W.GetCursorPos(out var np)) ShowMenu(np);
                return IntPtr.Zero;
            case W.WM_SIZE:
                if (_edit != IntPtr.Zero) DoLayout();
                W.InvalidateRect(Handle, IntPtr.Zero, false);
                return IntPtr.Zero;
            case W.WM_GETMINMAXINFO:
            {
                var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                mmi.MinTrackSize = new POINT((int)(140 * _scale), (int)(80 * _scale));
                Marshal.StructureToPtr(mmi, lParam, false);
                return IntPtr.Zero;
            }
            case W.WM_EXITSIZEMOVE:
                MovedOrSized?.Invoke(this);
                return IntPtr.Zero;
            case W.WM_DPICHANGED:
            {
                _scale = (W.HiWord(wParam) & 0xFFFF) / 96f;
                var r = Marshal.PtrToStructure<RECT>(lParam);
                W.SetWindowPos(Handle, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height, W.SWP_NOZORDER | W.SWP_NOACTIVATE);
                DoLayout();
                return IntPtr.Zero;
            }
            case W.WM_COMMAND:
                if ((((long)wParam >> 16) & 0xFFFF) == W.EN_CHANGE && lParam == _edit && !_styling)
                {
                    Restyle();
                    W.SetTimer(Handle, (UIntPtr)SaveTimer, 1000, IntPtr.Zero); // save 1 s after the last change
                }
                return IntPtr.Zero;
            case W.WM_NOTIFY:
            {
                var hdr = Marshal.PtrToStructure<NMHDR>(lParam);
                if (hdr.hwndFrom == _edit && hdr.code == W.EN_MSGFILTER)
                {
                    var mf = Marshal.PtrToStructure<MSGFILTER>(lParam);
                    if (OnEditMouse(mf.msg, mf.lParam)) return (IntPtr)1;
                }
                return IntPtr.Zero;
            }
            case W.WM_TIMER when (uint)wParam == SaveTimer:
                W.KillTimer(Handle, (UIntPtr)SaveTimer);
                Changed?.Invoke(this);
                return IntPtr.Zero;
            case W.WM_DESTROY:
                W.KillTimer(Handle, (UIntPtr)SaveTimer);
                if (_bgBrush != IntPtr.Zero) W.DeleteObject(_bgBrush);
                RichEditOle.Release(_textDoc);
                _textDoc = IntPtr.Zero;
                return IntPtr.Zero;
        }
        return base.OnMessage(msg, wParam, lParam);
    }

    /// <summary>Saves now if a save is pending (at exit).</summary>
    public void Flush()
    {
        if (!IsAlive) return;
        W.KillTimer(Handle, (UIntPtr)SaveTimer);
        Text = ReadText();
        Changed?.Invoke(this);
    }
}
