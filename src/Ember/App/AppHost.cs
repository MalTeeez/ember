using System.Runtime.InteropServices;
using Ember.Core.Config;
using Ember.Core.Render;
using Ember.Core.Status;
using Ember.Native;
using Ember.Sensors;
using Ember.Ui;

namespace Ember.App;

/// <summary>
/// The application: a hidden host window (tray, hotkey, session and power messages),
/// the status widget, and the sampler. All UI work runs on the main thread.
/// </summary>
internal sealed partial class AppHost : Win32Window, IDisposable
{
    public const string HostClass = "Ember.Host";
    private const uint WM_SNAPSHOT = W.WM_APP + 1, WM_TRAY = W.WM_APP + 2, WM_CLAUDE = W.WM_APP + 3;
    private const int CmdNewNote = 1, CmdEdit = 2, CmdSettings = 3, CmdShow = 4, CmdPreview = 5, CmdOpenLog = 6, CmdExit = 9;

    public static readonly uint ActivateMessage = W.RegisterWindowMessage("Ember.Activate");
    private static readonly uint TaskbarCreated = W.RegisterWindowMessage("TaskbarCreated");

    private volatile EmberSettings _settings;
    private readonly StatusComposer _composer = new();
    private readonly WidgetWindow _widget = new();
    private TrayIcon? _tray;
    private Sampler? _sampler;
    private Snapshot? _pending;
    private bool _locked, _displayOff;
    private IntPtr _powerNotify;

    /// <summary>Settings for other threads (always replaced, never changed in place).</summary>
    public EmberSettings Settings => _settings;

    public AppHost()
    {
        _settings = SettingsStore.Load(Paths.Settings);
        Log.Enabled = _settings.DebugLog;
        SetDecisionLog(_settings);
        Log.Write("start " + typeof(AppHost).Assembly.GetName().Version);
    }

    public void Run()
    {
        RegisterClass(HostClass, IntPtr.Zero, IntPtr.Zero);
        CreateHandle(HostClass, "Ember", W.WS_EX_TOOLWINDOW, W.WS_POPUP, 0, 0, 0, 0, IntPtr.Zero);
        // Ember runs elevated (scheduled task, highest privileges). Allow these messages from
        // Explorer and from a non-elevated second instance through UIPI.
        W.ChangeWindowMessageFilterEx(Handle, TaskbarCreated, W.MSGFLT_ALLOW, IntPtr.Zero);
        W.ChangeWindowMessageFilterEx(Handle, ActivateMessage, W.MSGFLT_ALLOW, IntPtr.Zero);

        WidgetWindow.AttachToDesktop = _settings.Layout.OnDesktop;
        _widget.Settings = _settings;
        _widget.Create();
        _widget.Moved += () =>
        {
            ApplySettings(_widget.Settings, save: true, fromWidget: true);
            // The open settings window keeps its own copy: give it the new position, so its next
            // change does not move the widget back.
            _settingsWindow?.SyncPosition(_settings);
        };
        WireWidget();

        _tray = new TrayIcon(Handle, WM_TRAY);
        _tray.Add();

        W.WTSRegisterSessionNotification(Handle, W.NOTIFY_FOR_THIS_SESSION);
        var g = W.GUID_CONSOLE_DISPLAY_STATE;
        _powerNotify = W.RegisterPowerSettingNotification(Handle, ref g, 0);

        StartFeatures();

        _sampler = new Sampler(() => _settings, () => _composer.Needs) { Decorate = Decorate };
        _sampler.Sampled += snap =>
        {
            Interlocked.Exchange(ref _pending, snap);
            W.PostMessage(Handle, WM_SNAPSHOT, IntPtr.Zero, IntPtr.Zero);
        };
        _sampler.Start();

        MessageLoop();
    }

    private void MessageLoop()
    {
        while (W.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (Ember.Ui.ProcessPopup.PreTranslate(ref msg)) continue;
            if (IsDialogMessageForOpenWindows(ref msg)) continue;
            W.TranslateMessage(ref msg);
            W.DispatchMessage(ref msg);
        }
    }

    /// <summary>Extended debug log: the composer reports its show/hide and kept-space decisions.</summary>
    private void SetDecisionLog(EmberSettings s)
        => _composer.Trace = s.DebugLog && s.DebugDecisions ? m => Log.Write("decide: " + m) : null;

    /// <summary>Settings changed (from the settings window or a widget drag). Applies at once.</summary>
    public void ApplySettings(EmberSettings s, bool save, bool fromWidget = false)
    {
        s.Normalize();
        var copy = SettingsStore.Clone(s);
        _settings = copy;
        Log.Enabled = copy.DebugLog;
        SetDecisionLog(copy);
        if (save)
        {
            try { SettingsStore.Save(Paths.Settings, copy); }
            catch (Exception ex) { Log.Write("save settings failed: " + ex.Message); }
        }
        _widget.Settings = SettingsStore.Clone(copy);
        if (WidgetWindow.AttachToDesktop != copy.Layout.OnDesktop)
        {
            WidgetWindow.AttachToDesktop = copy.Layout.OnDesktop;
            _widget.Reattach();
            ReattachFeatures(); // notes follow the same choice
        }
        OnSettingsChanged(copy);
        if (!fromWidget) _widget.Invalidate();
        // The next sample (now) composes with the new settings. Composing the last snapshot
        // again would add a duplicate sample to the graphs.
        _sampler?.Poke();
    }

    private void OnSnapshot()
    {
        var snap = Interlocked.Exchange(ref _pending, null);
        if (snap == null) return;
        var lines = _composer.Compose(snap, _settings, ComposeContext());
        _widget.Render(lines);
    }

    private void UpdateIdle()
    {
        bool idle = _locked || _displayOff;
        if (idle)
        {
            _sampler?.Pause();
            PauseFeatures();
            Log.Write("idle: sampling stopped");
        }
        else
        {
            _composer.Reset();
            _sampler?.Resume();
            ResumeFeatures();
            Log.Write("active: sampling started");
        }
    }

    private void ShowTrayMenu(POINT pt)
    {
        var items = new List<(int, string, bool)>
        {
            (CmdShow, "Show widget", !_widget.Hidden),
            (CmdNewNote, "New note", false),
        };
        items.AddRange(MinimizedNoteMenuItems());
        items.AddRange(new (int, string, bool)[]
        {
            (0, "", false),
            (CmdEdit, "Edit layout", _widget.EditMode),
            (CmdPreview, "Show everything (theming preview)", _settings.PreviewAll),
            (CmdSettings, "Settings…", false),
            (CmdOpenLog, "Open log", false),
            (0, "", false),
            (CmdExit, "Exit", false),
        });
        int cmd = TrayIcon.ShowMenu(Handle, pt, items.ToArray());
        RunCommand(cmd);
    }

    private void RunCommand(int cmd)
    {
        switch (cmd)
        {
            case CmdNewNote: NewNote(); break;
            case CmdShow: _widget.SetHidden(!_widget.Hidden); break;
            case CmdPreview:
            {
                var s = SettingsStore.Clone(_settings);
                s.PreviewAll = !s.PreviewAll;
                ApplySettings(s, save: true);
                _settingsWindow?.Reload(_settings);
                break;
            }
            case >= CmdRestoreNoteBase: RestoreNoteByMenu(cmd - CmdRestoreNoteBase); break;
            case CmdEdit: _widget.SetEditMode(!_widget.EditMode); break;
            case CmdSettings: OpenSettings(); break;
            case CmdOpenLog: Paths.OpenLog(); break;
            case CmdExit: W.DestroyWindow(Handle); break;
        }
    }

    private void ShowWidgetMenu(POINT pt)
    {
        int cmd = TrayIcon.ShowMenu(Handle, pt,
            (CmdSettings, "Settings…", false),
            (CmdNewNote, "New note", false),
            (CmdPreview, "Show everything (theming preview)", _settings.PreviewAll),
            (CmdEdit, "Done (leave edit mode)", false));
        RunCommand(cmd);
    }

    protected override IntPtr OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_SNAPSHOT:
                OnSnapshot();
                return IntPtr.Zero;

            case WM_TRAY:
            {
                uint ev = (uint)((long)lParam & 0xFFFF);
                // NOTIFYICON_VERSION_4: event in LOWORD(lParam), anchor point in wParam.
                // (WM_RBUTTONUP/WM_LBUTTONUP also arrive; handling them too would open the menu twice.)
                if (ev is W.WM_CONTEXTMENU or W.NIN_SELECT or W.NIN_KEYSELECT)
                    ShowTrayMenu(new POINT(W.LoWord(wParam), W.HiWord(wParam)));
                return IntPtr.Zero;
            }

            case WM_CLAUDE:
                _sampler?.Poke();
                return IntPtr.Zero;

            case W.WM_HOTKEY:
                if ((int)wParam == NewNoteHotkeyId) NewNote();
                return IntPtr.Zero;

            case W.WM_WTSSESSION_CHANGE:
                if ((uint)wParam == W.WTS_SESSION_LOCK) { _locked = true; UpdateIdle(); }
                else if ((uint)wParam == W.WTS_SESSION_UNLOCK) { _locked = false; UpdateIdle(); }
                return IntPtr.Zero;

            case W.WM_POWERBROADCAST:
                if ((uint)wParam == W.PBT_POWERSETTINGCHANGE && lParam != IntPtr.Zero)
                {
                    var ps = Marshal.PtrToStructure<POWERBROADCAST_SETTING>(lParam);
                    if (ps.PowerSetting == W.GUID_CONSOLE_DISPLAY_STATE)
                    {
                        bool off = ps.Data == 0; // 0 = off, 1 = on, 2 = dimmed
                        if (off != _displayOff) { _displayOff = off; UpdateIdle(); }
                    }
                }
                return (IntPtr)1;

            case W.WM_DISPLAYCHANGE:
            case W.WM_SETTINGCHANGE:
                _widget.Invalidate();
                return IntPtr.Zero;

            case W.WM_DESTROY:
                Shutdown();
                W.PostQuitMessage(0);
                return IntPtr.Zero;
        }

        if (msg == TaskbarCreated && msg != 0)
        {
            // Explorer restarted: the desktop window and the tray are new.
            Log.Write("TaskbarCreated: attach again");
            _tray?.Add();
            _widget.Reattach();
            ReattachFeatures();
            return IntPtr.Zero;
        }
        if (msg == ActivateMessage && msg != 0)
        {
            _widget.BringToFront();
            BringFeaturesToFront();
            return IntPtr.Zero;
        }
        return base.OnMessage(msg, wParam, lParam);
    }

    private bool _shutdown;

    private void Shutdown()
    {
        if (_shutdown) return;
        _shutdown = true;
        StopFeatures();
        _sampler?.Dispose();
        _sampler = null;
        if (_powerNotify != IntPtr.Zero) W.UnregisterPowerSettingNotification(_powerNotify);
        W.WTSUnRegisterSessionNotification(Handle);
        _tray?.Dispose();
        _widget.Destroy();
    }

    public void Dispose() => Shutdown();
}
