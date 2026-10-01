using System.Runtime.InteropServices;
using Ember.App;
using Ember.Core.Config;
using Ember.Core.Notes;
using Ember.Core.Render;
using Ember.Native;

namespace Ember.Ui;

/// <summary>
/// One sticky note: a borderless window on the desktop layer with a RichEdit control.
/// Markdown is styled as you type; markers stay visible but dim. Click a checkbox to tick it.
/// Drag the top strip to move; drag an edge to resize.
/// </summary>
internal sealed class NoteWindow : Win32Window
{
    private const string ClassName = "Ember.Note";
    private const uint SaveTimer = 1;
    private const int CmdNew = 1, CmdDelete = 2;
    private static IntPtr _bgBrush;
    private static bool _richEditLoaded;

    private IntPtr _edit;
    private bool _styling;
    private float _scale = 1;
    private LayoutSettings _layout;

    public string Id { get; }
    public string Text { get; private set; }
    public event Action<NoteWindow>? Changed;      // text changed (save after 1 s)
    public event Action<NoteWindow>? MovedOrSized;
    public event Action<NoteWindow>? DeleteRequested;
    public event Action? NewRequested;

    public NoteWindow(string id, string text, LayoutSettings layout)
    {
        Id = id;
        Text = text;
        _layout = layout;
    }

    public void Create(RECT r)
    {
        if (!_richEditLoaded)
        {
            W.LoadLibrary("Msftedit.dll");
            _richEditLoaded = true;
        }
        if (_bgBrush == IntPtr.Zero) _bgBrush = W.CreateSolidBrush(Palette.Background.ToColorRef());
        RegisterClass(ClassName, IntPtr.Zero, _bgBrush);
        CreateHandle(ClassName, "Ember note", W.WS_EX_LAYERED | W.WS_EX_TOOLWINDOW, W.WS_POPUP | W.WS_CLIPCHILDREN,
            r.Left, r.Top, Math.Max(80, r.Width), Math.Max(60, r.Height), WidgetWindow.DesktopOwner());
        _scale = W.GetDpiForWindow(Handle) / 96f;

        _edit = W.CreateWindowEx(0, "RICHEDIT50W", null,
            W.WS_CHILD | W.WS_VISIBLE | W.ES_MULTILINE | W.ES_AUTOVSCROLL | W.ES_WANTRETURN | W.ES_NOHIDESEL,
            0, 0, 10, 10, Handle, (IntPtr)100, Instance, IntPtr.Zero);
        W.SendMessage(_edit, W.EM_EXLIMITTEXT, IntPtr.Zero, (IntPtr)(1 << 20));
        W.SendMessage(_edit, W.EM_SETBKGNDCOLOR, IntPtr.Zero, (IntPtr)Palette.Background.ToColorRef());
        W.SendMessage(_edit, W.WM_SETTEXT, IntPtr.Zero, Text);
        W.SendMessage(_edit, W.EM_SETEVENTMASK, IntPtr.Zero, (IntPtr)(W.ENM_CHANGE | W.ENM_MOUSEEVENTS));
        ApplyLayout(_layout);
        Layout();
        W.ShowWindow(Handle, W.SW_SHOWNOACTIVATE);
        W.SetWindowPos(Handle, W.HWND_BOTTOM, 0, 0, 0, 0, W.SWP_NOMOVE | W.SWP_NOSIZE | W.SWP_NOACTIVATE);
    }

    public void Focus()
    {
        W.SetForegroundWindow(Handle);
        W.SetFocus(_edit);
    }

    public void ApplyLayout(LayoutSettings layout)
    {
        _layout = layout;
        W.SetLayeredWindowAttributes(Handle, 0, (byte)Math.Round(255 * Math.Clamp(layout.Opacity, 10, 100) / 100.0), W.LWA_ALPHA);
        Restyle();
    }

    public void Reattach()
    {
        if (!IsAlive) return;
        W.SetWindowLongPtr(Handle, W.GWLP_HWNDPARENT, WidgetWindow.DesktopOwner());
        W.ShowWindow(Handle, W.SW_SHOWNOACTIVATE);
    }

    public void BringToFront()
    {
        if (IsAlive) W.SetWindowPos(Handle, W.HWND_TOP, 0, 0, 0, 0, W.SWP_NOMOVE | W.SWP_NOSIZE | W.SWP_NOACTIVATE);
    }

    public RECT Bounds => W.GetWindowRect(Handle, out var r) ? r : default;

    private int Margin => (int)(6 * _scale);
    private int DragStrip => (int)(14 * _scale);

    private void Layout()
    {
        W.GetClientRect(Handle, out var rc);
        W.MoveWindow(_edit, Margin, DragStrip, Math.Max(10, rc.Width - 2 * Margin), Math.Max(10, rc.Height - DragStrip - Margin), true);
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

    private CHARFORMAT2 Format(MdStyle style)
    {
        var cf = new CHARFORMAT2
        {
            cbSize = (uint)Marshal.SizeOf<CHARFORMAT2>(),
            dwMask = W.CFM_COLOR | W.CFM_BOLD | W.CFM_ITALIC | W.CFM_STRIKEOUT | W.CFM_BACKCOLOR,
            szFaceName = "",
        };
        bool dim = (style & (MdStyle.Marker | MdStyle.Done)) != 0;
        cf.crTextColor = (dim ? Palette.Dim : Palette.Bright).ToColorRef();
        cf.crBackColor = ((style & MdStyle.Code) != 0 ? Palette.CodeBackground : Palette.Background).ToColorRef();
        if ((style & MdStyle.Bold) != 0) cf.dwEffects |= W.CFE_BOLD;
        if ((style & MdStyle.Italic) != 0) cf.dwEffects |= W.CFE_ITALIC;
        if ((style & MdStyle.Done) != 0 && (style & MdStyle.Marker) == 0) cf.dwEffects |= W.CFE_STRIKEOUT;
        return cf;
    }

    /// <summary>Styles all text again. Keeps the selection; no change events while styling.</summary>
    private void Restyle()
    {
        if (_edit == IntPtr.Zero || _styling) return;
        _styling = true;
        try
        {
            string text = ReadText();
            Text = text;
            var mask = W.SendMessage(_edit, W.EM_GETEVENTMASK, IntPtr.Zero, IntPtr.Zero);
            W.SendMessage(_edit, W.EM_SETEVENTMASK, IntPtr.Zero, IntPtr.Zero);
            W.SendMessage(_edit, W.WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            var sel = new CHARRANGE();
            W.SendMessage(_edit, W.EM_EXGETSEL, IntPtr.Zero, ref sel);

            var all = Format(MdStyle.Normal);
            all.dwMask |= W.CFM_FACE | W.CFM_SIZE | W.CFM_CHARSET;
            all.szFaceName = FaceName();
            all.yHeight = (int)Math.Round(_layout.FontSize * 20);
            all.bCharSet = 1; // DEFAULT_CHARSET
            W.SendMessage(_edit, W.EM_SETCHARFORMAT, (IntPtr)W.SCF_ALL, ref all);

            foreach (var span in MarkdownStyler.Style(text))
            {
                var range = new CHARRANGE { cpMin = span.Start, cpMax = span.End };
                W.SendMessage(_edit, W.EM_EXSETSEL, IntPtr.Zero, ref range);
                var cf = Format(span.Style);
                W.SendMessage(_edit, W.EM_SETCHARFORMAT, (IntPtr)W.SCF_SELECTION, ref cf);
            }

            W.SendMessage(_edit, W.EM_EXSETSEL, IntPtr.Zero, ref sel);
            // Typing continues in the normal style.
            var typing = Format(MdStyle.Normal);
            W.SendMessage(_edit, W.EM_SETCHARFORMAT, (IntPtr)W.SCF_SELECTION, ref typing);
            W.SendMessage(_edit, W.WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            W.InvalidateRect(_edit, IntPtr.Zero, true);
            W.SendMessage(_edit, W.EM_SETEVENTMASK, IntPtr.Zero, mask);
        }
        finally { _styling = false; }
    }

    private string FaceName() => string.IsNullOrWhiteSpace(_layout.Font) ? "Cascadia Mono" : _layout.Font;

    // ───────── Mouse: checkboxes and menu ─────────

    private bool OnEditMouse(uint msg, IntPtr lParam)
    {
        if (msg == W.WM_LBUTTONDOWN)
        {
            var pt = new POINT(W.LoWord(lParam), W.HiWord(lParam));
            int index = (int)W.SendMessage(_edit, W.EM_CHARFROMPOS, IntPtr.Zero, ref pt);
            string text = ReadText();
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
            W.GetCursorPos(out var sp);
            ShowMenu(sp);
            return true;
        }
        return false;
    }

    private void ShowMenu(POINT sp)
    {
        int cmd = TrayIcon.ShowMenu(Handle, sp, (CmdNew, "New note", false), (0, "", false), (CmdDelete, "Delete note", false));
        if (cmd == CmdNew) NewRequested?.Invoke();
        else if (cmd == CmdDelete)
        {
            bool empty = string.IsNullOrWhiteSpace(Text);
            if (empty || W.MessageBox(Handle, "Delete this note?", "Ember", W.MB_OKCANCEL | W.MB_ICONWARNING | W.MB_DEFBUTTON2) == W.IDOK)
                DeleteRequested?.Invoke(this);
        }
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
                bool l = pt.X < e, r = pt.X >= rc.Width - e, t = pt.Y < e / 2 + 1, b = pt.Y >= rc.Height - e;
                if (t && l) return (IntPtr)W.HTTOPLEFT;
                if (t && r) return (IntPtr)W.HTTOPRIGHT;
                if (b && l) return (IntPtr)W.HTBOTTOMLEFT;
                if (b && r) return (IntPtr)W.HTBOTTOMRIGHT;
                if (l) return (IntPtr)W.HTLEFT;
                if (r) return (IntPtr)W.HTRIGHT;
                if (t) return (IntPtr)W.HTTOP;
                if (b) return (IntPtr)W.HTBOTTOM;
                if (pt.Y < DragStrip) return (IntPtr)W.HTCAPTION;
                return (IntPtr)W.HTCLIENT;
            }
            case W.WM_NCRBUTTONUP:
                if (W.GetCursorPos(out var np)) ShowMenu(np);
                return IntPtr.Zero;
            case W.WM_SIZE:
                if (_edit != IntPtr.Zero) Layout();
                return IntPtr.Zero;
            case W.WM_GETMINMAXINFO:
            {
                var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                mmi.MinTrackSize = new POINT((int)(100 * _scale), (int)(60 * _scale));
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
                Layout();
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
