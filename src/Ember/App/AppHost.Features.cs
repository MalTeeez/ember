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

    private void StartFeatures()
    {
        _claude = new ClaudePoller(Paths.ClaudeCredentials);
        _claude.Configure(_settings.Items.Claude, _settings.Intervals.ClaudeSeconds);
        if (_settings.Items.Claude) _claude.Start();
    }

    private void PauseFeatures()
    {
        _claude?.Pause();
    }

    private void ResumeFeatures()
    {
        _claude?.Resume();
    }
    private void ReattachFeatures() { }
    private void BringFeaturesToFront() { }
    private void StopFeatures()
    {
        _settingsWindow?.Destroy();
        _claude?.Dispose();
        _claude = null;
    }

    private void OnSettingsChanged(EmberSettings s)
    {
        _claude?.Configure(s.Items.Claude, s.Intervals.ClaudeSeconds);
    }
    private void NewNote() { }
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
        return s;
    }
}
