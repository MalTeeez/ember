using Ember.Core.Config;
using Ember.Core.Status;
using Ember.Native;

namespace Ember.App;

// Feature hooks. Filled in by the later steps (Claude, notes, settings, process actions).
internal sealed partial class AppHost
{
    private const int NewNoteHotkeyId = 1;

    private void StartFeatures() { }
    private void PauseFeatures() { }
    private void ResumeFeatures() { }
    private void ReattachFeatures() { }
    private void BringFeaturesToFront() { }
    private void StopFeatures() { }
    private void OnSettingsChanged(EmberSettings s) { }
    private void NewNote() { }
    private void OpenSettings() { }
    private void WireWidget() => _widget.WidgetMenu += ShowWidgetMenu;
    private bool IsDialogMessageForOpenWindows(ref MSG msg) => false;
    private Snapshot Decorate(Snapshot s) => s;
}
