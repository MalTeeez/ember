using System.Runtime.InteropServices;

namespace Ember.Native;

// Win32 declarations used by Ember. x64 only.
#pragma warning disable CA1069, IDE1006

internal delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
internal delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

[StructLayout(LayoutKind.Sequential)]
internal struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

[StructLayout(LayoutKind.Sequential)]
internal struct SIZE { public int CX, CY; public SIZE(int cx, int cy) { CX = cx; CY = cy; } }

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left, Top, Right, Bottom;
    public RECT(int l, int t, int r, int b) { Left = l; Top = t; Right = r; Bottom = b; }
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DWM_BLURBEHIND
{
    public uint dwFlags;
    public int fEnable;
    public IntPtr hRgnBlur;
    public int fTransitionOnMaximized;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MSG
{
    public IntPtr hwnd;
    public uint message;
    public IntPtr wParam, lParam;
    public uint time;
    public POINT pt;
    public uint lPrivate;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WNDCLASSEX
{
    public uint cbSize;
    public uint style;
    public IntPtr lpfnWndProc;
    public int cbClsExtra, cbWndExtra;
    public IntPtr hInstance, hIcon, hCursor, hbrBackground;
    public string? lpszMenuName;
    public string lpszClassName;
    public IntPtr hIconSm;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BLENDFUNCTION
{
    public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFOHEADER
{
    public uint biSize;
    public int biWidth, biHeight;
    public ushort biPlanes, biBitCount;
    public uint biCompression, biSizeImage;
    public int biXPelsPerMeter, biYPelsPerMeter;
    public uint biClrUsed, biClrImportant;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MONITORINFO
{
    public uint cbSize;
    public RECT rcMonitor, rcWork;
    public uint dwFlags;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct NOTIFYICONDATA
{
    public uint cbSize;
    public IntPtr hWnd;
    public uint uID, uFlags, uCallbackMessage;
    public IntPtr hIcon;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
    public uint dwState, dwStateMask;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
    public uint uVersion;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
    public uint dwInfoFlags;
    public Guid guidItem;
    public IntPtr hBalloonIcon;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MEMORYSTATUSEX
{
    public uint dwLength, dwMemoryLoad;
    public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PERFORMANCE_INFORMATION
{
    public uint cb;
    public UIntPtr CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache,
        KernelTotal, KernelPaged, KernelNonpaged, PageSize;
    public uint HandleCount, ProcessCount, ThreadCount;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FILETIME { public uint Low, High; public long Value => ((long)High << 32) | Low; }

[StructLayout(LayoutKind.Sequential)]
internal struct MINMAXINFO { public POINT Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }

[StructLayout(LayoutKind.Sequential)]
internal struct NMHDR { public IntPtr hwndFrom; public UIntPtr idFrom; public int code; }

// richedit.h is packed with pshpack4: on x64 wParam is at offset 28, lParam at 36.
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct MSGFILTER { public NMHDR nmhdr; public uint msg; public IntPtr wParam, lParam; }

[StructLayout(LayoutKind.Sequential)]
internal struct CHARRANGE { public int cpMin, cpMax; }

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct CHARFORMAT2
{
    public uint cbSize, dwMask, dwEffects;
    public int yHeight, yOffset;
    public uint crTextColor;
    public byte bCharSet, bPitchAndFamily;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szFaceName;
    public ushort wWeight;
    public short sSpacing;
    public uint crBackColor;
    public uint lcid, dwReserved;
    public short sStyle;
    public ushort wKerning;
    public byte bUnderlineType, bAnimation, bRevAuthor, bUnderlineColor;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal unsafe struct PARAFORMAT2
{
    public uint cbSize, dwMask;
    public ushort wNumbering, wEffects;
    public int dxStartIndent, dxRightIndent, dxOffset;
    public ushort wAlignment;
    public short cTabCount;
    public fixed int rgxTabs[32];
    public int dySpaceBefore, dySpaceAfter, dyLineSpacing;
    public short sStyle;
    public byte bLineSpacingRule, bOutlineLevel;
    public ushort wShadingWeight, wShadingStyle, wNumberingStart, wNumberingStyle, wNumberingTab, wBorderSpace, wBorderWidth, wBorders;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct GETTEXTEX { public uint cb, flags, codepage; public IntPtr lpDefaultChar, lpUsedDefChar; }

[StructLayout(LayoutKind.Sequential)]
internal struct GETTEXTLENGTHEX { public uint flags, codepage; }

[StructLayout(LayoutKind.Sequential)]
internal struct POWERBROADCAST_SETTING { public Guid PowerSetting; public uint DataLength; public uint Data; }

[StructLayout(LayoutKind.Sequential)]
internal struct SCROLLINFO { public uint cbSize, fMask; public int nMin, nMax; public uint nPage; public int nPos, nTrackPos; }

[StructLayout(LayoutKind.Sequential)]
internal struct CHOOSECOLOR
{
    public uint lStructSize;
    public IntPtr hwndOwner, hInstance;
    public uint rgbResult;
    public IntPtr lpCustColors;
    public uint Flags;
    public IntPtr lCustData, lpfnHook, lpTemplateName;
}

[StructLayout(LayoutKind.Sequential)]
internal struct OPENFILENAME
{
    public uint lStructSize;
    public IntPtr hwndOwner, hInstance, lpstrFilter, lpstrCustomFilter;
    public uint nMaxCustFilter, nFilterIndex;
    public IntPtr lpstrFile;
    public uint nMaxFile;
    public IntPtr lpstrFileTitle;
    public uint nMaxFileTitle;
    public IntPtr lpstrInitialDir, lpstrTitle;
    public uint Flags;
    public ushort nFileOffset, nFileExtension;
    public IntPtr lpstrDefExt, lCustData, lpfnHook, lpTemplateName, pvReserved;
    public uint dwReserved, FlagsEx;
}

[StructLayout(LayoutKind.Sequential)]
internal struct INITCOMMONCONTROLSEX { public uint dwSize, dwICC; }

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct SHELLEXECUTEINFO
{
    public uint cbSize, fMask;
    public IntPtr hwnd;
    public string? lpVerb, lpFile, lpParameters, lpDirectory;
    public int nShow;
    public IntPtr hInstApp, lpIDList;
    public string? lpClass;
    public IntPtr hkeyClass;
    public uint dwHotKey;
    public IntPtr hIconOrMonitor, hProcess;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ACCENT_POLICY { public int AccentState, AccentFlags; public uint GradientColor; public int AnimationId; }

[StructLayout(LayoutKind.Sequential)]
internal struct WINDOWCOMPOSITIONATTRIBDATA { public int Attrib; public IntPtr pvData; public int cbData; }

[StructLayout(LayoutKind.Sequential)]
internal struct TRACKMOUSEEVENT { public uint cbSize, dwFlags; public IntPtr hwndTrack; public uint dwHoverTime; }

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PAINTSTRUCT
{
    public IntPtr hdc;
    public int fErase;
    public RECT rcPaint;
    public int fRestore, fIncUpdate;
    public fixed byte rgbReserved[32];
}

internal static class W
{
    // Window messages
    public const uint WM_CREATE = 0x0001, WM_DESTROY = 0x0002, WM_MOVE = 0x0003, WM_SIZE = 0x0005, WM_ACTIVATE = 0x0006,
        WM_SETFOCUS = 0x0007, WM_KILLFOCUS = 0x0008, WM_SETREDRAW = 0x000B, WM_SETTEXT = 0x000C, WM_GETTEXT = 0x000D,
        WM_GETTEXTLENGTH = 0x000E, WM_PAINT = 0x000F, WM_CLOSE = 0x0010, WM_QUIT = 0x0012, WM_ERASEBKGND = 0x0014,
        WM_SHOWWINDOW = 0x0018, WM_SETCURSOR = 0x0020, WM_GETMINMAXINFO = 0x0024, WM_WINDOWPOSCHANGING = 0x0046,
        WM_NOTIFY = 0x004E, WM_SETFONT = 0x0030, WM_NCHITTEST = 0x0084, WM_NCRBUTTONUP = 0x00A5, WM_NCLBUTTONDOWN = 0x00A1,
        WM_KEYDOWN = 0x0100, WM_CHAR = 0x0102, WM_COMMAND = 0x0111, WM_SYSCOMMAND = 0x0112, WM_TIMER = 0x0113,
        WM_VSCROLL = 0x0115, WM_CTLCOLOREDIT = 0x0133, WM_CTLCOLORSTATIC = 0x0138, WM_CTLCOLORBTN = 0x0135,
        WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_RBUTTONDOWN = 0x0204,
        WM_RBUTTONUP = 0x0205, WM_MOUSEWHEEL = 0x020A, WM_EXITSIZEMOVE = 0x0232, WM_HOTKEY = 0x0312,
        WM_POWERBROADCAST = 0x0218, WM_WTSSESSION_CHANGE = 0x02B1, WM_DPICHANGED = 0x02E0,
        WM_DISPLAYCHANGE = 0x007E, WM_SETTINGCHANGE = 0x001A, WM_APP = 0x8000, WM_USER = 0x0400,
        WM_CONTEXTMENU = 0x007B, WM_NULL = 0x0000;

    // Window styles
    public const uint WS_OVERLAPPED = 0x00000000, WS_POPUP = 0x80000000, WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000,
        WS_CAPTION = 0x00C00000, WS_SYSMENU = 0x00080000, WS_THICKFRAME = 0x00040000, WS_MINIMIZEBOX = 0x00020000,
        WS_VSCROLL = 0x00200000, WS_TABSTOP = 0x00010000, WS_BORDER = 0x00800000, WS_CLIPCHILDREN = 0x02000000,
        WS_GROUP = 0x00020000;
    public const uint WS_EX_LAYERED = 0x00080000, WS_EX_TRANSPARENT = 0x00000020, WS_EX_TOOLWINDOW = 0x00000080,
        WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x00000008, WS_EX_CONTROLPARENT = 0x00010000,
        WS_EX_DLGMODALFRAME = 0x00000001;

    // Control styles
    public const uint ES_MULTILINE = 0x0004, ES_AUTOVSCROLL = 0x0040, ES_AUTOHSCROLL = 0x0080, ES_READONLY = 0x0800,
        ES_WANTRETURN = 0x1000, ES_NOHIDESEL = 0x0100;
    public const uint BS_AUTOCHECKBOX = 0x0003, BS_PUSHBUTTON = 0x0000;
    public const uint CBS_DROPDOWNLIST = 0x0003;
    public const uint SS_LEFT = 0x0000, SS_CENTERIMAGE = 0x0200;
    public const uint BM_GETCHECK = 0x00F0, BM_SETCHECK = 0x00F1, BST_CHECKED = 1;
    public const uint CB_ADDSTRING = 0x0143, CB_GETCURSEL = 0x0147, CB_SETCURSEL = 0x014E;
    public const int BN_CLICKED = 0, EN_CHANGE = 0x0300, EN_KILLFOCUS = 0x0200, CBN_SELCHANGE = 1;

    public const int GWL_STYLE = -16, GWL_EXSTYLE = -20, GWLP_HWNDPARENT = -8;
    public const int SW_HIDE = 0, SW_SHOW = 5, SW_SHOWNOACTIVATE = 4, SW_SHOWNORMAL = 1;
    public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010,
        SWP_FRAMECHANGED = 0x0020, SWP_SHOWWINDOW = 0x0040, SWP_NOOWNERZORDER = 0x0200,
        SWP_NOREDRAW = 0x0008, SWP_NOCOPYBITS = 0x0100;
    public static readonly IntPtr HWND_BOTTOM = new(1), HWND_TOP = IntPtr.Zero;

    public const int HTCLIENT = 1, HTCAPTION = 2, HTTRANSPARENT = -1, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12,
        HTTOPLEFT = 13, HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

    public const uint ULW_ALPHA = 0x2;
    public const byte AC_SRC_OVER = 0, AC_SRC_ALPHA = 1;
    public const uint LWA_ALPHA = 0x2;

    public const uint MF_STRING = 0x0, MF_SEPARATOR = 0x800, MF_CHECKED = 0x8, MF_GRAYED = 0x1;
    public const uint TPM_RETURNCMD = 0x0100, TPM_RIGHTBUTTON = 0x0002, TPM_NONOTIFY = 0x0080;

    public const uint MB_OKCANCEL = 0x1, MB_YESNO = 0x4, MB_ICONWARNING = 0x30, MB_ICONINFORMATION = 0x40,
        MB_DEFBUTTON2 = 0x100, MB_TOPMOST = 0x40000, MB_SETFOREGROUND = 0x10000;
    public const int IDOK = 1, IDYES = 6;

    public const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    public const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_SHOWTIP = 0x80;
    public const uint NOTIFYICON_VERSION_4 = 4;
    public const uint NIN_SELECT = 0x400, NIN_KEYSELECT = 0x401;

    public const uint MONITOR_DEFAULTTONEAREST = 2, MONITOR_DEFAULTTOPRIMARY = 1, MONITORINFOF_PRIMARY = 1;
    public const int MDT_EFFECTIVE_DPI = 0;

    public const uint WTS_SESSION_LOCK = 7, WTS_SESSION_UNLOCK = 8, NOTIFY_FOR_THIS_SESSION = 0;
    public const uint PBT_POWERSETTINGCHANGE = 0x8013, PBT_APMRESUMEAUTOMATIC = 0x12, PBT_APMSUSPEND = 0x4;
    public static readonly Guid GUID_CONSOLE_DISPLAY_STATE = new("6FE69556-704A-47A0-8F24-C28D936FDA47");

    public const int IDC_ARROW = 32512, IDC_HAND = 32649, IDC_IBEAM = 32513;
    public const int SB_LINEUP = 0, SB_LINEDOWN = 1, SB_PAGEUP = 2, SB_PAGEDOWN = 3, SB_THUMBTRACK = 5, SB_THUMBPOSITION = 4, SB_VERT = 1;
    public const uint SIF_RANGE = 1, SIF_PAGE = 2, SIF_POS = 4, SIF_TRACKPOS = 0x10, SIF_ALL = 0x17;
    public const uint SW_SCROLLCHILDREN = 1, SW_INVALIDATE = 2, SW_ERASE = 4;

    public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

    // RichEdit
    public const uint EM_GETSEL = 0x00B0, EM_SETSEL = 0x00B1, EM_REPLACESEL = 0x00C2, EM_EXGETSEL = WM_USER + 52,
        EM_EXSETSEL = WM_USER + 55, EM_SETCHARFORMAT = WM_USER + 68, EM_SETEVENTMASK = WM_USER + 69,
        EM_GETEVENTMASK = WM_USER + 59, EM_SETBKGNDCOLOR = WM_USER + 67, EM_CHARFROMPOS = 0x00D7,
        EM_GETTEXTEX = WM_USER + 94, EM_GETTEXTLENGTHEX = WM_USER + 95, EM_SETTEXTMODE = WM_USER + 89,
        EM_SETMARGINS = 0x00D3, EM_STOPGROUPTYPING = WM_USER + 88, EM_SETUNDOLIMIT = WM_USER + 82,
        EM_HIDESELECTION = WM_USER + 63, EM_SETLIMITTEXT = 0x00C5, EM_EXLIMITTEXT = WM_USER + 53;
    public const uint EM_SETPARAFORMAT = WM_USER + 71, PFM_STARTINDENT = 0x1, PFM_OFFSET = 0x4, PFM_SPACEBEFORE = 0x40, PFM_SPACEAFTER = 0x80;
    public const uint CFE_UNDERLINE = 0x4;
    [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
    public const int VK_CONTROL = 0x11, VK_LBUTTON = 0x01;
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] public static extern bool RedrawWindow(IntPtr hwnd, IntPtr rect, IntPtr rgn, uint flags);
    public const uint RDW_INVALIDATE = 0x1, RDW_ERASE = 0x4, RDW_ALLCHILDREN = 0x80, RDW_UPDATENOW = 0x100, RDW_FRAME = 0x400;
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DWM_BLURBEHIND bb);
    public const uint DWM_BB_ENABLE = 1, DWM_BB_BLURREGION = 2;
    public const int DWMWA_NCRENDERING_POLICY = 2, DWMNCRP_DISABLED = 1, DWMWA_WINDOW_CORNER_PREFERENCE = 33,
        DWMWCP_DONOTROUND = 1, DWMWCP_ROUND = 2, DWMWCP_ROUNDSMALL = 3;
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, ref PARAFORMAT2 lParam);
    public const uint ENM_CHANGE = 0x1, ENM_MOUSEEVENTS = 0x20000, ENM_KEYEVENTS = 0x10000;
    public const int EN_MSGFILTER = 0x0700;
    public const uint SCF_SELECTION = 0x1, SCF_ALL = 0x4, SCF_DEFAULT = 0x0;
    public const uint CFM_BOLD = 0x1, CFM_ITALIC = 0x2, CFM_UNDERLINE = 0x4, CFM_STRIKEOUT = 0x8, CFM_COLOR = 0x40000000,
        CFM_FACE = 0x20000000, CFM_SIZE = 0x80000000, CFM_BACKCOLOR = 0x04000000, CFM_CHARSET = 0x08000000;
    public const uint CFE_BOLD = 0x1, CFE_ITALIC = 0x2, CFE_STRIKEOUT = 0x8, CFE_AUTOCOLOR = 0x40000000, CFE_AUTOBACKCOLOR = 0x04000000;
    public const uint GT_DEFAULT = 0, GTL_DEFAULT = 0, GTL_NUMCHARS = 8, GTL_PRECISE = 2;
    public const uint EC_LEFTMARGIN = 1, EC_RIGHTMARGIN = 2;

    public const uint CS_HREDRAW = 2, CS_VREDRAW = 1, CS_DBLCLKS = 8;
    public const int COLOR_WINDOW = 5, COLOR_BTNFACE = 15;
    public const uint MOD_NOREPEAT = 0x4000;

    // user32
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowEx(uint exStyle, string className, string? windowName, uint style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool IsDialogMessage(IntPtr hDlg, ref MSG msg);
    [DllImport("user32.dll")] public static extern void PostQuitMessage(int code);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, string lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, ref CHARFORMAT2 lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, ref CHARRANGE lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, ref POINT lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hwnd, uint msg, ref GETTEXTEX wParam, char[] lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hwnd, uint msg, ref GETTEXTLENGTHEX wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, char[] buf, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SetWindowText(IntPtr hwnd, string text);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string? cls, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint flags);
    [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] public static extern UIntPtr SetTimer(IntPtr hwnd, UIntPtr id, uint ms, IntPtr proc);
    [DllImport("user32.dll")] public static extern bool KillTimer(IntPtr hwnd, UIntPtr id);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr hwnd, ref POINT pt);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hwnd, ref POINT pt);
    [DllImport("user32.dll")] public static extern IntPtr LoadCursor(IntPtr inst, IntPtr id);
    [DllImport("user32.dll")] public static extern IntPtr SetCursor(IntPtr cursor);
    [DllImport("user32.dll")] public static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string? text);
    [DllImport("user32.dll")] public static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
    [DllImport("user32.dll")] public static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int MessageBox(IntPtr hwnd, string text, string caption, uint type);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO info);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] public static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid guid, uint flags);
    [DllImport("user32.dll")] public static extern bool UnregisterPowerSettingNotification(IntPtr handle);
    [DllImport("user32.dll")] public static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);
    [DllImport("user32.dll")] public static extern bool EnableWindow(IntPtr hwnd, bool enable);
    [DllImport("user32.dll")] public static extern int SetScrollInfo(IntPtr hwnd, int bar, ref SCROLLINFO si, bool redraw);
    [DllImport("user32.dll")] public static extern bool GetScrollInfo(IntPtr hwnd, int bar, ref SCROLLINFO si);
    [DllImport("user32.dll")] public static extern int ScrollWindowEx(IntPtr hwnd, int dx, int dy, IntPtr scroll, IntPtr clip, IntPtr update, IntPtr rcUpdate, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] public static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern bool IsChild(IntPtr parent, IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetSysColorBrush(int index);
    [DllImport("user32.dll")] public static extern bool AdjustWindowRectExForDpi(ref RECT rect, uint style, bool menu, uint exStyle, uint dpi);
    [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr hwnd, int id);
    [DllImport("user32.dll")] public static extern IntPtr SetCapture(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint msg, uint action, IntPtr info);
    public const uint MSGFLT_ALLOW = 1;
    [DllImport("user32.dll")] public static extern int SetWindowRgn(IntPtr hwnd, IntPtr rgn, bool redraw);
    [DllImport("user32.dll")] public static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);
    [DllImport("user32.dll")] public static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT e);
    [DllImport("user32.dll")] public static extern bool PtInRect(ref RECT r, POINT p);
    [DllImport("user32.dll")] public static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT ps);
    [DllImport("user32.dll")] public static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    public const uint WM_MOUSELEAVE = 0x02A3, TME_LEAVE = 0x2;
    public const int WCA_ACCENT_POLICY = 19;
    public const int ACCENT_DISABLED = 0, ACCENT_ENABLE_BLURBEHIND = 3, ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;
    [DllImport("user32.dll")] public static extern bool ReleaseCapture();

    // gdi32
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] public static extern uint SetTextColor(IntPtr hdc, uint color);
    [DllImport("gdi32.dll")] public static extern uint SetBkColor(IntPtr hdc, uint color);
    [DllImport("gdi32.dll")] public static extern int SetBkMode(IntPtr hdc, int mode);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr CreateFont(int h, int w, int esc, int orient, int weight,
        uint italic, uint underline, uint strike, uint charset, uint outPrecision, uint clipPrecision, uint quality, uint pitch, string face);

    [DllImport("gdi32.dll")] public static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern uint GetGlyphIndices(IntPtr hdc, string text, int count, ushort[] indices, uint flags);
    public const uint GGI_MARK_NONEXISTING_GLYPHS = 1;

    // kernel32
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr LoadLibrary(string name);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool TerminateProcess(IntPtr h, uint code);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool QueryFullProcessImageName(IntPtr h, uint flags, char[] buf, ref int size);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetProcessTimes(IntPtr h, out FILETIME create, out FILETIME exit, out FILETIME kernel, out FILETIME user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool GetDiskFreeSpaceEx(string dir, out ulong freeToCaller, out ulong total, out ulong totalFree);
    [DllImport("kernel32.dll")] public static extern uint GetLogicalDrives();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern uint GetDriveType(string root);
    [DllImport("kernel32.dll")] public static extern uint SetErrorMode(uint mode);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetSystemCpuSetInformation(IntPtr info, uint length, out uint returned, IntPtr process, uint flags);
    [DllImport("kernel32.dll")] public static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);
    [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] public static extern bool SetPriorityClass(IntPtr process, uint priority);
    public const uint PROCESS_TERMINATE = 0x0001, PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, PROCESS_VM_READ = 0x0010;
    public const uint DRIVE_FIXED = 3, SEM_FAILCRITICALERRORS = 1;
    public const uint BELOW_NORMAL_PRIORITY_CLASS = 0x4000;

    // psapi
    [DllImport("psapi.dll", SetLastError = true)] public static extern bool GetPerformanceInfo(ref PERFORMANCE_INFORMATION info, uint size);

    // shell32
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern bool Shell_NotifyIcon(uint msg, ref NOTIFYICONDATA data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr ShellExecute(IntPtr hwnd, string? op, string file, string? args, string? dir, int show);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);
    public const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;

    [DllImport("comdlg32.dll", EntryPoint = "ChooseColorW")] public static extern bool ChooseColor(ref CHOOSECOLOR cc);
    [DllImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW")] public static extern bool GetOpenFileName(ref OPENFILENAME ofn);
    [DllImport("comdlg32.dll", EntryPoint = "GetSaveFileNameW")] public static extern bool GetSaveFileName(ref OPENFILENAME ofn);
    [DllImport("comctl32.dll")] public static extern bool InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);
    public const uint CC_RGBINIT = 0x1, CC_FULLOPEN = 0x2;
    public const uint OFN_OVERWRITEPROMPT = 0x2, OFN_PATHMUSTEXIST = 0x800, OFN_FILEMUSTEXIST = 0x1000, OFN_NOCHANGEDIR = 0x8;
    public const uint ICC_BAR_CLASSES = 0x4, ICC_STANDARD_CLASSES = 0x4000;
    public const uint TBM_GETPOS = WM_USER, TBM_SETPOS = WM_USER + 5, TBM_SETRANGE = WM_USER + 6, TBS_HORZ = 0, TBS_NOTICKS = 0x10;
    public const uint WM_HSCROLL = 0x0114;
    public const uint LB_ADDSTRING = 0x0180, LB_RESETCONTENT = 0x0184, LB_SETCURSEL = 0x0186, LB_GETCURSEL = 0x0188, LB_GETTOPINDEX = 0x018E, LB_SETTOPINDEX = 0x0197;
    public const uint LBS_NOTIFY = 0x1, LBS_NOINTEGRALHEIGHT = 0x100;
    public const int LBN_SELCHANGE = 1, LBN_DBLCLK = 2;

    // shcore
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr mon, int type, out uint dpiX, out uint dpiY);

    // wtsapi32
    [DllImport("wtsapi32.dll")] public static extern bool WTSRegisterSessionNotification(IntPtr hwnd, uint flags);
    [DllImport("wtsapi32.dll")] public static extern bool WTSUnRegisterSessionNotification(IntPtr hwnd);

    // ntdll
    [DllImport("ntdll.dll")] public static extern int NtQuerySystemInformation(int cls, IntPtr buffer, int length, out int returned);
    [DllImport("ntdll.dll")] public static extern int NtQueryInformationProcess(IntPtr process, int cls, IntPtr buffer, int length, out int returned);

    // helpers
    public static int LoWord(IntPtr v) => (short)((long)v & 0xFFFF);
    public static int HiWord(IntPtr v) => (short)(((long)v >> 16) & 0xFFFF);
    public static IntPtr MakeLParam(int lo, int hi) => (IntPtr)((hi << 16) | (lo & 0xFFFF));
}
