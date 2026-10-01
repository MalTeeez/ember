using Ember.Core.Config;
using Ember.Core.Render;
using Ember.Core.Status;
using Ember.Native;
using Ember.Sensors;
using Ember.Ui;

namespace Ember.App;

// Features: Claude, ping, notes, settings window, hotkey, widget buttons and process actions.
internal sealed partial class AppHost
{
    private const int NewNoteHotkeyId = 1;
    private const int CmdRestoreNoteBase = 100;

    private ClaudePoller? _claude;
    private NotesManager? _notes;
    private PingMonitor? _ping;
    private SettingsWindow? _settingsWindow;
    private string _hotkey = "";
    private string _noteThemeKey = "";

    private static bool PingOn(EmberSettings s) => s.IsOn("net.quality") || s.PreviewAll;
    private static bool ClaudeOn(EmberSettings s) => s.IsOn("claude.5h") || s.IsOn("claude.week") || s.IsOn("claude.models");
    private static string NoteThemeKey(EmberSettings s) => $"{s.Layout.Font}|{s.Layout.FontSize}|{ThemeStore.Serialize(s.Theme)}";

    private void StartFeatures()
    {
        _notes = new NotesManager(SettingsStore.Clone(_settings));
        _notes.MinimizedChanged += () => _sampler?.Poke();
        _notes.LoadAll();
        _noteThemeKey = NoteThemeKey(_settings);
        RegisterNoteHotkey(_settings.Notes.NewNoteHotkey);

        _ping = new PingMonitor();
        _ping.Configure(PingOn(_settings), _settings.Network.InternetHost);
        _claude = new ClaudePoller(Paths.ClaudeCredentials);
        _claude.Configure(ClaudeOn(_settings), _settings.Intervals.ClaudeSeconds);
        if (ClaudeOn(_settings)) _claude.Start();
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
        Tooltip.HideShared();
    }

    private void OnSettingsChanged(EmberSettings s)
    {
        _claude?.Configure(ClaudeOn(s), s.Intervals.ClaudeSeconds);
        RegisterNoteHotkey(s.Notes.NewNoteHotkey);
        _ping?.Configure(PingOn(s), s.Network.InternetHost);
        string key = NoteThemeKey(s);
        if (key != _noteThemeKey)
        {
            _noteThemeKey = key;
            _notes?.ApplySettings(SettingsStore.Clone(s));
        }
    }

    private void NewNote() => _notes?.New();

    private ComposeContext ComposeContext()
        => new() { MinimizedNotes = _notes?.Minimized() ?? Array.Empty<(string, string)>() };

    private IEnumerable<(int, string, bool)> MinimizedNoteMenuItems()
    {
        var list = _notes?.Minimized() ?? Array.Empty<(string, string)>();
        for (int i = 0; i < list.Count && i < 50; i++)
        {
            string title = string.IsNullOrWhiteSpace(list[i].Title) ? "Note" : list[i].Title;
            yield return (CmdRestoreNoteBase + i, "Restore note: " + (title.Length > 40 ? title[..40] + "…" : title), false);
        }
    }

    private void RestoreNoteByMenu(int index)
    {
        var list = _notes?.Minimized();
        if (list != null && index >= 0 && index < list.Count) _notes!.Restore(list[index].Id);
    }

    /// <summary>Opens the settings window (created now, destroyed on close).</summary>
    private void OpenSettings()
    {
        if (_settingsWindow is { IsAlive: true } open)
        {
            W.ShowWindow(open.Handle, W.SW_SHOWNORMAL);
            W.SetForegroundWindow(open.Handle);
            return;
        }
        _settingsWindow = new SettingsWindow(_settings, s => ApplySettings(s, save: true));
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
        _widget.HitClicked += OnHit;
        _widget.ProcessMenu += (pid, pt) => ProcessActions.ShowMenu(Handle, pid, pt, () => _sampler?.GpuByPid, _composer.Colors);
    }

    /// <summary>A click on a widget button or line.</summary>
    private void OnHit(Hit hit, POINT pt)
    {
        int.TryParse(hit.Arg, out int pid);
        switch (hit.Action)
        {
            case HitAction.ProcessDetails: ProcessPopup.Show(pid, pt, _sampler?.GpuByPid, _composer.Colors); break;
            case HitAction.ProcessProperties: ProcessActions.Properties(pid); break;
            case HitAction.ProcessKill: ProcessActions.EndProcess(Handle, pid); break;
            case HitAction.NewNote: NewNote(); break;
            case HitAction.HideWidget: _widget.SetHidden(true); break;
            case HitAction.RestoreNote: _notes?.Restore(hit.Arg); _sampler?.Poke(); break;
        }
    }

    /// <summary>Tab key navigation in the settings window.</summary>
    private bool IsDialogMessageForOpenWindows(ref MSG msg)
    {
        if (_settingsWindow is not { IsAlive: true } sw) return false;
        if (msg.hwnd != sw.Handle && !W.IsChild(sw.Handle, msg.hwnd)) return false;
        return W.IsDialogMessage(sw.Handle, ref msg);
    }

    /// <summary>Runs on the sampler thread: adds the Claude lines and network quality to each snapshot.</summary>
    private Snapshot Decorate(Snapshot s)
    {
        var cfg = _settings;
        if (ClaudeOn(cfg)) s.Claude = _claude?.Lines(DateTimeOffset.UtcNow);
        if (PingOn(cfg)) s.Quality = _ping?.Quality();
        return s;
    }
}
