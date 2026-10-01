using System.Runtime.InteropServices;
using Ember.Core.Render;
using Ember.Native;

namespace Ember.Ui;

/// <summary>Notification-area icon. The icon is drawn with GDI+ (an amber dot), so no resource file is needed.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly IntPtr _owner;
    private readonly uint _callback;
    private IntPtr _icon;
    private bool _added;
    private const uint Id = 1;

    public TrayIcon(IntPtr owner, uint callbackMessage)
    {
        _owner = owner;
        _callback = callbackMessage;
    }

    /// <summary>Adds the icon (again after an Explorer restart).</summary>
    public void Add()
    {
        if (_icon == IntPtr.Zero) _icon = CreateIcon();
        var data = Data();
        data.uFlags = W.NIF_MESSAGE | W.NIF_ICON | W.NIF_TIP | W.NIF_SHOWTIP;
        if (_added) W.Shell_NotifyIcon(W.NIM_DELETE, ref data);
        _added = W.Shell_NotifyIcon(W.NIM_ADD, ref data);
        data.uVersion = W.NOTIFYICON_VERSION_4;
        W.Shell_NotifyIcon(W.NIM_SETVERSION, ref data);
    }

    private NOTIFYICONDATA Data() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _owner,
        uID = Id,
        uCallbackMessage = _callback,
        hIcon = _icon,
        szTip = "Ember",
        szInfo = "",
        szInfoTitle = "",
    };

    private static IntPtr CreateIcon()
    {
        int size = Math.Max(16, W.GetSystemMetrics(49)); // SM_CXSMICON
        Gdip.GdipCreateBitmapFromScan0(size, size, 0, Gdip.PixelFormat32bppPARGB, IntPtr.Zero, out var bmp);
        Gdip.GdipGetImageGraphicsContext(bmp, out var g);
        Gdip.GdipSetSmoothingMode(g, Gdip.SmoothingModeAntiAlias);
        Gdip.GdipGraphicsClear(g, 0);
        Gdip.GdipCreateSolidFill(Palette.Red.ToArgb(), out var outer);
        Gdip.GdipFillEllipse(g, outer, size * 0.1f, size * 0.1f, size * 0.8f, size * 0.8f);
        Gdip.GdipCreateSolidFill(Palette.Amber.ToArgb(), out var inner);
        Gdip.GdipFillEllipse(g, inner, size * 0.3f, size * 0.3f, size * 0.4f, size * 0.4f);
        Gdip.GdipDeleteBrush(outer);
        Gdip.GdipDeleteBrush(inner);
        Gdip.GdipDeleteGraphics(g);
        Gdip.GdipCreateHICONFromBitmap(bmp, out var icon);
        Gdip.GdipDisposeImage(bmp);
        return icon;
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = Data();
            W.Shell_NotifyIcon(W.NIM_DELETE, ref data);
            _added = false;
        }
        if (_icon != IntPtr.Zero) { W.DestroyIcon(_icon); _icon = IntPtr.Zero; }
    }

    /// <summary>Shows a popup menu and returns the chosen command (0 = none).</summary>
    public static int ShowMenu(IntPtr owner, POINT pt, params (int Id, string Text, bool Checked)[] items)
    {
        var menu = W.CreatePopupMenu();
        try
        {
            foreach (var (id, text, check) in items)
            {
                if (id == 0) W.AppendMenu(menu, W.MF_SEPARATOR, UIntPtr.Zero, null);
                else W.AppendMenu(menu, W.MF_STRING | (check ? W.MF_CHECKED : 0), (UIntPtr)id, text);
            }
            W.SetForegroundWindow(owner);
            int cmd = W.TrackPopupMenu(menu, W.TPM_RETURNCMD | W.TPM_RIGHTBUTTON | W.TPM_NONOTIFY, pt.X, pt.Y, 0, owner, IntPtr.Zero);
            W.PostMessage(owner, W.WM_NULL, IntPtr.Zero, IntPtr.Zero);
            return cmd;
        }
        finally { W.DestroyMenu(menu); }
    }
}
