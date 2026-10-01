using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Ember.App;
using Ember.Core.Render;
using Ember.Core.Util;
using Ember.Native;
using Ember.Sensors;

namespace Ember.Ui;

/// <summary>Process actions: end, end tree, open file location, file properties.</summary>
internal static class ProcessActions
{
    public static void EndProcess(IntPtr owner, int pid)
    {
        string name = ProcessInfo.Name(pid) ?? ("PID " + pid);
        if (W.MessageBox(owner, $"End process {name} (PID {pid})?\n\nUnsaved data in this process will be lost.",
                "Ember", W.MB_OKCANCEL | W.MB_ICONWARNING | W.MB_DEFBUTTON2 | W.MB_SETFOREGROUND) != W.IDOK) return;
        if (!ProcessInfo.Kill(pid)) Log.Write($"end process {pid} failed");
    }

    /// <summary>Ends the descendants first, then the process.</summary>
    public static void EndTree(int pid)
    {
        var all = ProcessTracker.Snapshot();
        var children = ProcessTree.Descendants(pid, all);
        children.Reverse(); // deepest first
        foreach (var c in children) ProcessInfo.Kill(c);
        if (!ProcessInfo.Kill(pid)) Log.Write($"end process tree {pid} failed");
    }

    public static void OpenLocation(int pid)
    {
        var path = ProcessInfo.ImagePath(pid);
        if (path != null) W.ShellExecute(IntPtr.Zero, "open", "explorer.exe", $"/select,\"{path}\"", null, W.SW_SHOWNORMAL);
    }

    /// <summary>Opens the Explorer "Properties" dialog of the executable.</summary>
    public static void Properties(int pid)
    {
        var path = ProcessInfo.ImagePath(pid);
        if (path == null) return;
        var info = new SHELLEXECUTEINFO
        {
            cbSize = (uint)Marshal.SizeOf<SHELLEXECUTEINFO>(),
            fMask = W.SEE_MASK_INVOKEIDLIST,
            lpVerb = "properties",
            lpFile = path,
            nShow = W.SW_SHOWNORMAL,
        };
        if (!W.ShellExecuteEx(ref info)) Log.Write($"properties of {path} failed: {Marshal.GetLastWin32Error()}");
    }

    private const int CmdDetails = 1, CmdEnd = 2, CmdTree = 3, CmdLocation = 4, CmdProps = 5;

    /// <summary>Right-click menu of a process line.</summary>
    public static void ShowMenu(IntPtr owner, int pid, POINT pt, Func<Dictionary<int, double>?> gpu, ThemeColors colors)
    {
        string name = ProcessInfo.Name(pid) ?? ("PID " + pid);
        int cmd = TrayIcon.ShowMenu(owner, pt,
            (CmdDetails, "Details of " + name, false),
            (CmdProps, "File properties", false),
            (CmdLocation, "Open file location", false),
            (0, "", false),
            (CmdEnd, "End process", false),
            (CmdTree, "End process tree", false));
        switch (cmd)
        {
            case CmdDetails: ProcessPopup.Show(pid, pt, gpu(), colors); break;
            case CmdEnd: EndProcess(owner, pid); break;
            case CmdTree: EndTree(pid); break;
            case CmdLocation: OpenLocation(pid); break;
            case CmdProps: Properties(pid); break;
        }
    }
}

/// <summary>
/// Details window of a process: custom painted in the theme colours. Header with name and PID,
/// rows (path, command line, CPU, RAM, GPU, disk, start time), and buttons. Drag by the header.
/// Closes with Esc, the × button, or when it loses focus.
/// </summary>
internal sealed class ProcessPopup : Win32Window
{
    private const string ClassName = "Ember.ProcessPopup";
    private static ProcessPopup? _open;

    private readonly int _pid;
    private readonly ThemeColors _c;
    private string _title = "", _subtitle = "";
    private readonly List<(string Label, string Value)> _rows = new();
    private readonly List<(RECT Rect, string Text, Action Run, bool Danger)> _buttons = new();
    private RECT _closeRect;
    private int _hover = -1;
    private float _scale = 1;
    private int _headerH;

    private ProcessPopup(int pid, ThemeColors c)
    {
        _pid = pid;
        _c = c;
    }

    /// <summary>Shows the details of a process near the point. One popup at a time.</summary>
    public static void Show(int pid, POINT at, Dictionary<int, double>? gpuByPid, ThemeColors colors)
    {
        _open?.Destroy();
        var p = new ProcessPopup(pid, colors);
        p.Collect(gpuByPid);
        p.Create(at);
        _open = p;
    }

    private void Collect(Dictionary<int, double>? gpuByPid)
    {
        var a = SysInfo.Processes()?.FirstOrDefault(p => p.Pid == _pid);
        var t0 = Stopwatch.GetTimestamp();
        Thread.Sleep(250);
        var b = SysInfo.Processes()?.FirstOrDefault(p => p.Pid == _pid);
        double sec = Stopwatch.GetElapsedTime(t0).TotalSeconds;
        var inv = CultureInfo.InvariantCulture;
        if (b is not { } p)
        {
            _title = "PID " + _pid.ToString(inv);
            _subtitle = "The process has ended.";
            return;
        }
        _title = p.Name;
        _subtitle = $"PID {_pid.ToString(inv)}  ·  parent {p.ParentPid.ToString(inv)}";
        _rows.Add(("Path", ProcessInfo.ImagePath(_pid) ?? "—"));
        _rows.Add(("Command", ProcessInfo.CommandLine(_pid) ?? "—"));
        if (a is { } old && old.CreateTime == p.CreateTime && sec > 0)
        {
            double cpu = (p.CpuTime - old.CpuTime) / 1e7 / sec / Environment.ProcessorCount * 100;
            double io = p.IoBytes >= old.IoBytes ? (p.IoBytes - old.IoBytes) / sec : 0;
            _rows.Add(("CPU", cpu.ToString("0.0", inv) + " %"));
            _rows.Add(("Disk", Format.BytesPerSecond(io) + "  (read + write, includes network)"));
        }
        _rows.Add(("RAM", Format.Bytes(p.PrivateBytes) + " private"));
        _rows.Add(("GPU", gpuByPid != null ? gpuByPid.GetValueOrDefault(_pid).ToString("0.0", inv) + " %" : "—"));
        _rows.Add(("Started", DateTime.FromFileTime(p.CreateTime).ToString("yyyy-MM-dd HH:mm:ss", inv)));
    }

    private void Create(POINT at)
    {
        RegisterClass(ClassName, IntPtr.Zero, IntPtr.Zero);
        var mon = W.MonitorFromPoint(at, W.MONITOR_DEFAULTTONEAREST);
        _scale = Monitors.Dpi(mon) / 96f;
        int w = S(520);
        int h = Layout(w);
        var work = Monitors.All().FirstOrDefault(m => m.Handle == mon).Work;
        int x = Math.Clamp(at.X - w / 2, work.Left, Math.Max(work.Left, work.Right - w));
        int y = Math.Clamp(at.Y + S(12), work.Top, Math.Max(work.Top, work.Bottom - h));
        CreateHandle(ClassName, "Ember — " + _title, W.WS_EX_TOOLWINDOW, W.WS_POPUP, x, y, w, h, IntPtr.Zero);
        W.SetWindowRgn(Handle, W.CreateRoundRectRgn(0, 0, w + 1, h + 1, S(16), S(16)), false);
        W.ShowWindow(Handle, W.SW_SHOW);
        W.SetForegroundWindow(Handle);
    }

    private int S(double v) => (int)Math.Round(v * _scale);

    /// <summary>Computes row heights (command line wraps) and button places; returns the window height.</summary>
    private int Layout(int w)
    {
        _headerH = S(58);
        int y = _headerH + S(6);
        _rowHeights.Clear();
        foreach (var (_, value) in _rows)
        {
            int h = Math.Max(S(20), (int)Math.Ceiling(Measure(value, w - S(120) - S(20), 12.5f)) + S(6));
            _rowHeights.Add(h);
            y += h;
        }
        y += S(12);
        _buttons.Clear();
        int bx = S(16), bh = S(30);
        void Button(string text, Action run, bool danger = false)
        {
            int bw = (int)Math.Ceiling(Measure(text, 1000, 12.5f, oneLine: true)) + S(24);
            _buttons.Add((new RECT(bx, y, bx + bw, y + bh), text, run, danger));
            bx += bw + S(8);
        }
        int pid = _pid;
        Button("Open location", () => ProcessActions.OpenLocation(pid));
        Button("Properties", () => ProcessActions.Properties(pid));
        Button("Copy", CopyDetails);
        Button("End process", () => { ProcessActions.EndProcess(Handle, pid); Destroy(); }, true);
        Button("End tree", () => { ProcessActions.EndTree(pid); Destroy(); }, true);
        _closeRect = new RECT(w - S(40), S(12), w - S(12), S(40));
        return y + bh + S(16);
    }

    private readonly List<int> _rowHeights = new();

    private float Measure(string text, float width, float sizePx, bool oneLine = false)
    {
        Gdip.GdipCreateFontFamilyFromName("Segoe UI", IntPtr.Zero, out var fam);
        Gdip.GdipCreateFont(fam, sizePx * _scale, Gdip.FontStyleRegular, Gdip.UnitPixel, out var font);
        Gdip.GdipCreateBitmapFromScan0(1, 1, 0, Gdip.PixelFormat32bppPARGB, IntPtr.Zero, out var bmp);
        Gdip.GdipGetImageGraphicsContext(bmp, out var g);
        var layout = new RectF(0, 0, width, 10000);
        Gdip.GdipMeasureString(g, text, text.Length, font, ref layout, IntPtr.Zero, out var box, out _, out _);
        Gdip.GdipDeleteGraphics(g);
        Gdip.GdipDisposeImage(bmp);
        Gdip.GdipDeleteFont(font);
        Gdip.GdipDeleteFontFamily(fam);
        return oneLine ? box.Width : box.Height;
    }

    private void CopyDetails()
    {
        var text = _title + " — " + _subtitle + "\r\n" + string.Join("\r\n", _rows.Select(r => r.Label + ": " + r.Value));
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c clip") { RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            if (p == null) return;
            p.StandardInput.Write(text);
            p.StandardInput.Close();
            p.WaitForExit(2000);
        }
        catch (Exception ex) { Log.Write("copy failed: " + ex.Message); }
    }

    private void Paint(IntPtr hdc)
    {
        W.GetClientRect(Handle, out var rc);
        int w = rc.Width, h = rc.Height;
        Gdip.GdipCreateBitmapFromScan0(w, h, 0, Gdip.PixelFormat32bppPARGB, IntPtr.Zero, out var bmp);
        Gdip.GdipGetImageGraphicsContext(bmp, out var g);
        Gdip.GdipSetSmoothingMode(g, Gdip.SmoothingModeAntiAlias);
        Gdip.GdipSetTextRenderingHint(g, 5); // ClearType on an opaque background

        var bg = Rgb.Parse(_c.Theme.Background.Color, Palette.Background);
        var bgSolid = Rgb.Lerp(bg, new Rgb(0, 0, 0), 0.15);
        Gdip.GdipGraphicsClear(g, bgSolid.ToArgb());
        Gdip.FillRoundRect(g, Rgb.Lerp(bgSolid, _c.Text, 0.06).ToArgb(), 0, 0, w, _headerH, 0);
        Gdip.DrawRoundRect(g, _c.Rule.ToArgb(), 1, 0.5f, 0.5f, w - 1, h - 1, S(8));

        Gdip.GdipCreateFontFamilyFromName("Segoe UI", IntPtr.Zero, out var fam);
        Gdip.GdipCreateFont(fam, 17 * _scale, Gdip.FontStyleBold, Gdip.UnitPixel, out var titleFont);
        Gdip.GdipCreateFont(fam, 12.5f * _scale, Gdip.FontStyleRegular, Gdip.UnitPixel, out var font);
        Gdip.GdipCreateFont(fam, 12.5f * _scale, Gdip.FontStyleBold, Gdip.UnitPixel, out var boldFont);
        Gdip.GdipCreateSolidFill(0xFFFFFFFF, out var brush);

        void Text(string s, IntPtr f, Rgb color, float x, float y, float width, float height = 1000)
        {
            Gdip.GdipSetSolidFillColor(brush, color.ToArgb());
            var r = new RectF(x, y, width, height);
            Gdip.GdipDrawString(g, s, s.Length, f, ref r, IntPtr.Zero, brush);
        }

        Text(_title, titleFont, _c.Header, S(16), S(9), w - S(70), S(26));
        Text(_subtitle, font, _c.Unit, S(16), S(33), w - S(70), S(20));
        // × close
        if (_hover == -2) Gdip.FillRoundRect(g, _c.Crit.ToArgb(0x40), _closeRect.Left, _closeRect.Top, _closeRect.Width, _closeRect.Height, S(6));
        Text("✕", font, _c.Icon, _closeRect.Left + S(9), _closeRect.Top + S(5), S(20), S(20));

        float y = _headerH + S(6);
        for (int i = 0; i < _rows.Count; i++)
        {
            Text(_rows[i].Label, font, _c.Label, S(16), y, S(100), S(20));
            Text(_rows[i].Value, i == 0 ? boldFont : font, _c.Text, S(120), y, w - S(140));
            y += _rowHeights[i];
        }

        for (int i = 0; i < _buttons.Count; i++)
        {
            var (r, text, _, danger) = _buttons[i];
            var accent = danger ? _c.Crit : _c.Cpu;
            byte fillA = (byte)(i == _hover ? 0x55 : 0x26);
            Gdip.FillRoundRect(g, accent.ToArgb(fillA), r.Left, r.Top, r.Width, r.Height, S(6));
            Gdip.DrawRoundRect(g, accent.ToArgb(0x80), 1, r.Left + 0.5f, r.Top + 0.5f, r.Width - 1, r.Height - 1, S(6));
            Text(text, font, danger ? _c.Crit : _c.Text, r.Left + S(12), r.Top + S(6), r.Width, r.Height);
        }

        Gdip.GdipDeleteBrush(brush);
        Gdip.GdipDeleteFont(titleFont);
        Gdip.GdipDeleteFont(font);
        Gdip.GdipDeleteFont(boldFont);
        Gdip.GdipDeleteFontFamily(fam);
        Gdip.GdipCreateFromHDC(hdc, out var screen);
        Gdip.GdipDrawImageI(screen, bmp, 0, 0);
        Gdip.GdipDeleteGraphics(screen);
        Gdip.GdipDeleteGraphics(g);
        Gdip.GdipDisposeImage(bmp);
    }

    private int ButtonAt(int x, int y)
    {
        if (_closeRect.Contains(x, y)) return -2;
        for (int i = 0; i < _buttons.Count; i++) if (_buttons[i].Rect.Contains(x, y)) return i;
        return -1;
    }

    protected override IntPtr OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case W.WM_PAINT:
            {
                var hdc = W.BeginPaint(Handle, out var ps);
                Paint(hdc);
                W.EndPaint(Handle, ref ps);
                return IntPtr.Zero;
            }
            case W.WM_ERASEBKGND:
                return (IntPtr)1;
            case W.WM_NCHITTEST:
            {
                var p = new POINT(W.LoWord(lParam), W.HiWord(lParam));
                W.ScreenToClient(Handle, ref p);
                if (p.Y < _headerH && ButtonAt(p.X, p.Y) == -1) return (IntPtr)W.HTCAPTION;
                return (IntPtr)W.HTCLIENT;
            }
            case W.WM_MOUSEMOVE:
            {
                int b = ButtonAt(W.LoWord(lParam), W.HiWord(lParam));
                if (b != _hover)
                {
                    _hover = b;
                    W.InvalidateRect(Handle, IntPtr.Zero, false);
                    var tme = new TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(), dwFlags = W.TME_LEAVE, hwndTrack = Handle };
                    W.TrackMouseEvent(ref tme);
                }
                return IntPtr.Zero;
            }
            case W.WM_MOUSELEAVE:
                _hover = -1;
                W.InvalidateRect(Handle, IntPtr.Zero, false);
                return IntPtr.Zero;
            case W.WM_SETCURSOR:
                if (W.GetCursorPos(out var cp))
                {
                    W.ScreenToClient(Handle, ref cp);
                    if (ButtonAt(cp.X, cp.Y) != -1) { W.SetCursor(W.LoadCursor(IntPtr.Zero, W.IDC_HAND)); return (IntPtr)1; }
                }
                break;
            case W.WM_LBUTTONUP:
            {
                int b = ButtonAt(W.LoWord(lParam), W.HiWord(lParam));
                if (b == -2) Destroy();
                else if (b >= 0) _buttons[b].Run();
                return IntPtr.Zero;
            }
            case W.WM_ACTIVATE:
                if (((long)wParam & 0xFFFF) == 0) W.PostMessage(Handle, W.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); // closes when it loses focus
                break;
            case W.WM_KEYDOWN:
                if ((int)wParam == 0x1B) Destroy(); // Esc
                return IntPtr.Zero;
            case W.WM_DESTROY:
                if (_open == this) _open = null;
                return IntPtr.Zero;
        }
        return base.OnMessage(msg, wParam, lParam);
    }
}
