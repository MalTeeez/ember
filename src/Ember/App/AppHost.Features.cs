using Ember.Core.Config;
using Ember.Core.Status;
using Ember.Native;
using Ember.Sensors;

namespace Ember.App;

// Feature hooks. Filled in by the later steps (Claude, notes, settings, process actions).
internal sealed partial class AppHost
{
    private const int NewNoteHotkeyId = 1;

    private ClaudePoller? _claude;
    private Ui.NotesManager? _notes;
    private PingMonitor? _ping;
    private string _hotkey = "";
    private string _noteLayoutKey = "";

    private static string NoteLayoutKey(EmberSettings s) => $"{s.Layout.Font}|{s.Layout.FontSize}|{s.Layout.Opacity}";

    private void StartFeatures()
    {
        _notes = new Ui.NotesManager(SettingsStore.Clone(_settings).Layout);
        _notes.LoadAll();
        _noteLayoutKey = NoteLayoutKey(_settings);
        RegisterNoteHotkey(_settings.Notes.NewNoteHotkey);

        _ping = new PingMonitor();
        _ping.Configure(_settings.Items.NetworkQuality && _settings.Items.Network, _settings.Network.InternetHost);
        _claude = new ClaudePoller(Paths.ClaudeCredentials);
        _claude.Configure(_settings.Items.Claude, _settings.Intervals.ClaudeSeconds);
        if (_settings.Items.Claude) _claude.Start();
    }

    private void PauseFeatures()
    {
        _claude?.Pause();
        _ping?.Pause();
    }

    private void ResumeFeatures()
    {
        _claude?.Resume();
        _ping?.Resume();
    }
    private void ReattachFeatures() => _notes?.Reattach();
    private void BringFeaturesToFront() => _notes?.BringToFront();

    /// <summary>Registers the global "new note" hotkey. An invalid or taken hotkey is logged and ignored.</summary>
    private void RegisterNoteHotkey(string text)
    {
        if (text == _hotkey) return;
        W.UnregisterHotKey(Handle, NewNoteHotkeyId);
        _hotkey = text;
        if (!Core.Util.Hotkey.TryParse(text, out uint mods, out uint vk)) return;
        if (!W.RegisterHotKey(Handle, NewNoteHotkeyId, mods | W.MOD_NOREPEAT, vk))
            Log.Write($"hotkey {text} is used by another program");
    }
    private void StopFeatures()
    {
        _settingsWindow?.Destroy();
        _notes?.CloseAll();
        W.UnregisterHotKey(Handle, NewNoteHotkeyId);
        _claude?.Dispose();
        _claude = null;
        _ping?.Dispose();
        _ping = null;
    }

    private void OnSettingsChanged(EmberSettings s)
    {
        _claude?.Configure(s.Items.Claude, s.Intervals.ClaudeSeconds);
        RegisterNoteHotkey(s.Notes.NewNoteHotkey);
        _ping?.Configure(s.Items.NetworkQuality && s.Items.Network, s.Network.InternetHost);
        string key = NoteLayoutKey(s);
        if (key != _noteLayoutKey)
        {
            _noteLayoutKey = key;
            _notes?.ApplyLayout(SettingsStore.Clone(s).Layout);
        }
    }

    private void NewNote() => _notes?.New();
    private Ui.SettingsWindow? _settingsWindow;

    /// <summary>Opens the settings window (created now, destroyed on close).</summary>
    private void OpenSettings()
    {
        if (_settingsWindow is { IsAlive: true } open)
        {
            W.ShowWindow(open.Handle, W.SW_SHOWNORMAL);
            W.SetForegroundWindow(open.Handle);
            return;
        }
        _settingsWindow = new Ui.SettingsWindow(_settings, s => ApplySettings(s, save: true));
        _settingsWindow.Closed += () =>
        {
            _settingsWindow = null;
            GC.Collect(); // release the window's managed memory now
        };
        _settingsWindow.Create();
    }
    private void WireWidget()
    {
        _widget.WidgetMenu += ShowWidgetMenu;
        _widget.ProcessClicked += (pid, pt) => Ui.ProcessPopup.Show(pid, pt, _sampler?.GpuByPid);
        _widget.ProcessMenu += (pid, pt) => Ui.ProcessPopup.ShowMenu(Handle, pid, pt);
    }
    /// <summary>Tab key navigation in the settings window.</summary>
    private bool IsDialogMessageForOpenWindows(ref MSG msg)
    {
        if (_settingsWindow is not { IsAlive: true } sw) return false;
        if (msg.hwnd != sw.Handle && !W.IsChild(sw.Handle, msg.hwnd)) return false;
        return W.IsDialogMessage(sw.Handle, ref msg);
    }
    /// <summary>Runs on the sampler thread: adds the Claude lines to each snapshot.</summary>
    private Snapshot Decorate(Snapshot s)
    {
        if (_settings.Items.Claude) s.Claude = _claude?.Lines(DateTimeOffset.UtcNow);
        if (_settings.Items.NetworkQuality) s.Quality = _ping?.Quality();
        return s;
    }
}
