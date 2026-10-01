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
        _claude?.Dispose();
        _claude = null;
    }

    private void OnSettingsChanged(EmberSettings s)
    {
        _claude?.Configure(s.Items.Claude, s.Intervals.ClaudeSeconds);
    }
    private void NewNote() { }
    private void OpenSettings() { }
    private void WireWidget() => _widget.WidgetMenu += ShowWidgetMenu;
    private bool IsDialogMessageForOpenWindows(ref MSG msg) => false;
    /// <summary>Runs on the sampler thread: adds the Claude lines to each snapshot.</summary>
    private Snapshot Decorate(Snapshot s)
    {
        if (_settings.Items.Claude) s.Claude = _claude?.Lines(DateTimeOffset.UtcNow);
        return s;
    }
}
