using System.Runtime.InteropServices;
using Ember.Native;

namespace Ember.App;

/// <summary>File locations.</summary>
internal static class Paths
{
    public static string AppData => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ember");
    public static string Settings => Path.Combine(AppData, "settings.json");
    public static string NotesDir => Path.Combine(AppData, "notes");
    public static string NotesIndex => Path.Combine(NotesDir, "notes.json");
    public static string LogFile => Path.Combine(AppData, "ember.log");
    public static string ClaudeCredentials => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");

    /// <summary>Opens the log in the default editor. Makes an empty log first if there is none.</summary>
    public static void OpenLog()
    {
        try
        {
            Directory.CreateDirectory(AppData);
            if (!File.Exists(LogFile))
                File.WriteAllText(LogFile, Log.Enabled ? "" : "Debug log is off. Turn it on in Settings > General > Debug.\r\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Open(LogFile);
    }

    /// <summary>Opens a file or folder with its default program.</summary>
    public static void Open(string path)
    {
        if ((long)W.ShellExecute(IntPtr.Zero, "open", path, null, null, W.SW_SHOWNORMAL) <= 32)
            W.ShellExecute(IntPtr.Zero, "open", "notepad.exe", "\"" + path + "\"", null, W.SW_SHOWNORMAL);
    }
}

/// <summary>Debug log. Off by default; turn on in Settings. The file is cut at 1 MB.</summary>
internal static class Log
{
    private static readonly object Gate = new();
    public static volatile bool Enabled;
    private const long MaxBytes = 1024 * 1024;

    public static void Write(string message)
    {
        if (!Enabled) return;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Paths.AppData);
                var fi = new FileInfo(Paths.LogFile);
                if (fi.Exists && fi.Length > MaxBytes)
                    File.Move(Paths.LogFile, Paths.LogFile + ".old", true);
                File.AppendAllText(Paths.LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never break the widget.
        }
    }

    /// <summary>Logs a source failure once until it works again.</summary>
    private static readonly HashSet<string> Failed = new();

    public static void SourceFailed(string source, Exception? ex = null)
    {
        lock (Failed)
        {
            if (!Failed.Add(source)) return;
        }
        Write($"source failed: {source}{(ex != null ? ": " + ex.GetType().Name + ": " + ex.Message : "")}");
    }

    public static void SourceOk(string source)
    {
        bool was;
        lock (Failed) was = Failed.Remove(source);
        if (was) Write($"source ok again: {source}");
    }
}

/// <summary>Base class for a Win32 window with a managed message handler.</summary>
internal abstract class Win32Window
{
    private static readonly WndProc Proc = StaticProc;
    private static readonly IntPtr ProcPtr = Marshal.GetFunctionPointerForDelegate(Proc);
    private static readonly Dictionary<IntPtr, Win32Window> Map = new();
    private static readonly HashSet<string> Registered = new();
    [ThreadStatic] private static Win32Window? _creating;

    public IntPtr Handle { get; private set; }
    public bool IsAlive => Handle != IntPtr.Zero && W.IsWindow(Handle);

    protected static IntPtr Instance => W.GetModuleHandle(null);

    protected static void RegisterClass(string name, IntPtr cursor, IntPtr background, uint style = 0)
    {
        if (Registered.Contains(name)) return;
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            style = style,
            lpfnWndProc = ProcPtr,
            hInstance = Instance,
            hCursor = cursor == IntPtr.Zero ? W.LoadCursor(IntPtr.Zero, W.IDC_ARROW) : cursor,
            hbrBackground = background,
            lpszClassName = name,
        };
        W.RegisterClassEx(ref wc);
        Registered.Add(name);
    }

    protected IntPtr CreateHandle(string cls, string? title, uint exStyle, uint style, int x, int y, int w, int h, IntPtr parent)
    {
        _creating = this;
        try
        {
            var hwnd = W.CreateWindowEx(exStyle, cls, title, style, x, y, w, h, parent, IntPtr.Zero, Instance, IntPtr.Zero);
            if (hwnd != IntPtr.Zero && Handle == IntPtr.Zero)
            {
                Handle = hwnd;
                Map[hwnd] = this;
            }
            return hwnd;
        }
        finally { _creating = null; }
    }

    protected virtual IntPtr OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
        => W.DefWindowProc(Handle, msg, wParam, lParam);

    protected virtual void OnDestroyed() { }

    public void Destroy()
    {
        if (Handle != IntPtr.Zero) W.DestroyWindow(Handle);
    }

    private static IntPtr StaticProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (!Map.TryGetValue(hwnd, out var win))
        {
            if (_creating == null) return W.DefWindowProc(hwnd, msg, wParam, lParam);
            win = _creating;
            win.Handle = hwnd;
            Map[hwnd] = win;
        }
        try
        {
            var r = win.OnMessage(msg, wParam, lParam);
            if (msg == 0x0082) // WM_NCDESTROY
            {
                Map.Remove(hwnd);
                win.Handle = IntPtr.Zero;
                win.OnDestroyed();
            }
            return r;
        }
        catch (Exception ex)
        {
            Log.Write($"window message 0x{msg:X4} failed: {ex}");
            return W.DefWindowProc(hwnd, msg, wParam, lParam);
        }
    }
}

/// <summary>Monitors, ordered: primary first, then the others from left to right.</summary>
internal static class Monitors
{
    public readonly record struct Info(IntPtr Handle, RECT Bounds, RECT Work, bool Primary);

    public static List<Info> All()
    {
        var list = new List<Info>();
        W.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr hdc, ref RECT r, IntPtr d) =>
        {
            var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            if (W.GetMonitorInfo(h, ref mi))
                list.Add(new Info(h, mi.rcMonitor, mi.rcWork, (mi.dwFlags & W.MONITORINFOF_PRIMARY) != 0));
            return true;
        }, IntPtr.Zero);
        return list.OrderByDescending(m => m.Primary).ThenBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top).ToList();
    }

    public static Info Get(int index)
    {
        var all = All();
        if (all.Count == 0) return new Info(IntPtr.Zero, new RECT(0, 0, 1920, 1080), new RECT(0, 0, 1920, 1080), true);
        return index >= 0 && index < all.Count ? all[index] : all[0];
    }

    public static int IndexOf(IntPtr monitor)
    {
        var all = All();
        int i = all.FindIndex(m => m.Handle == monitor);
        return Math.Max(0, i);
    }

    public static uint Dpi(IntPtr monitor)
    {
        try
        {
            if (monitor != IntPtr.Zero && W.GetDpiForMonitor(monitor, W.MDT_EFFECTIVE_DPI, out uint x, out _) == 0 && x > 0) return x;
        }
        catch (EntryPointNotFoundException) { }
        catch (DllNotFoundException) { }
        return 96;
    }
}
