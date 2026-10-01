using Ember.App;
using Ember.Native;
using Ember.Ui;

namespace Ember;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // Per-monitor DPI awareness v2 (also in app.manifest).
        try { W.SetProcessDpiAwarenessContext(W.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); }
        catch (EntryPointNotFoundException) { }

        // Single instance: a second start only brings the existing instance to the front.
        Mutex? mutex = null;
        bool first;
        try { mutex = new Mutex(true, @"Local\Ember.SingleInstance", out first); }
        catch (UnauthorizedAccessException) { first = false; } // the running instance is elevated
        if (!first)
        {
            var other = W.FindWindow(AppHost.HostClass, null);
            if (other != IntPtr.Zero) W.PostMessage(other, AppHost.ActivateMessage, IntPtr.Zero, IntPtr.Zero);
            return 0;
        }

        // A widget should never slow down the foreground app.
        W.SetPriorityClass(W.GetCurrentProcess(), W.BELOW_NORMAL_PRIORITY_CLASS);

        TextRenderer.Startup();
        try
        {
            using var host = new AppHost();
            host.Run();
        }
        catch (Exception ex)
        {
            Log.Enabled = true;
            Log.Write("fatal: " + ex);
            return 1;
        }
        finally
        {
            if (TextRenderer.GdipToken != IntPtr.Zero) Gdip.GdiplusShutdown(TextRenderer.GdipToken);
        }
        GC.KeepAlive(mutex);
        mutex?.Dispose();
        return 0;
    }
}
