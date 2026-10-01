using Ember.App;
using Ember.Core.Devices;
using Ember.Native;

namespace Ember.Sensors;

/// <summary>
/// Reads device batteries on a timer (default 60 s): Bluetooth devices every time, Razer receivers
/// every second time (each query talks to the receiver). Results are merged by device name.
/// </summary>
internal sealed class BatteryMonitor : IDisposable
{
    private readonly Timer _timer;
    private volatile IReadOnlyList<DeviceBattery>? _last;
    private List<DeviceBattery> _razer = new();
    private volatile bool _enabled, _paused, _busy;
    private TimeSpan _interval = TimeSpan.FromSeconds(60);
    private int _tick;

    public BatteryMonitor() => _timer = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);

    public IReadOnlyList<DeviceBattery>? Batteries => _enabled ? _last : null;

    public void Configure(bool enabled, double seconds)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(seconds, 10, 3600));
        bool changed = enabled != _enabled || interval != _interval;
        _enabled = enabled;
        _interval = interval;
        if (!changed) return;
        if (enabled && !_paused) _timer.Change(TimeSpan.FromSeconds(3), _interval);
        else
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            _last = null;
        }
    }

    public void Pause()
    {
        _paused = true;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public void Resume()
    {
        _paused = false;
        if (_enabled) _timer.Change(TimeSpan.FromSeconds(3), _interval);
    }

    private void Poll()
    {
        if (_busy || _paused || !_enabled) return;
        _busy = true;
        try
        {
            var all = new List<DeviceBattery>();
            try
            {
                all.AddRange(DeviceBatteries.Bluetooth());
                Log.SourceOk("battery.bluetooth");
            }
            catch (Exception ex) { Log.SourceFailed("battery.bluetooth", ex); }
            if (_tick++ % 2 == 0)
            {
                try
                {
                    _razer = DeviceBatteries.Razer();
                    Log.SourceOk("battery.razer");
                }
                catch (Exception ex)
                {
                    _razer = new();
                    Log.SourceFailed("battery.razer", ex);
                }
            }
            all.AddRange(_razer);
            _last = all.Count > 0 ? DeviceNames.Merge(all) : null;
        }
        finally { _busy = false; }
    }

    public void Dispose() => _timer.Dispose();
}
