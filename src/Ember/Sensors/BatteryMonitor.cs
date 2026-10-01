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
            // Device lists for the log (at start and when they change): help to find why a device is missing.
            var btTrace = Log.Enabled ? new List<string>() : null;
            try
            {
                all.AddRange(DeviceBatteries.Bluetooth(btTrace));
                Log.SourceOk("battery.bluetooth");
            }
            catch (Exception ex) { Log.SourceFailed("battery.bluetooth", ex); }
            LogTrace("Bluetooth devices", btTrace, ref _lastBt);
            if (_tick++ % 2 == 0)
            {
                var razerTrace = Log.Enabled ? new List<string>() : null;
                try
                {
                    _razer = DeviceBatteries.Razer(razerTrace);
                    Log.SourceOk("battery.razer");
                }
                catch (Exception ex)
                {
                    _razer = new();
                    Log.SourceFailed("battery.razer", ex);
                }
                LogTrace("Razer HID interfaces (vendor 1532)", razerTrace, ref _lastRazer);
            }
            all.AddRange(_razer);
            _last = all.Count > 0 ? DeviceNames.Merge(all) : null;
        }
        finally { _busy = false; }
    }

    private string _lastBt = "", _lastRazer = "";

    private static void LogTrace(string title, List<string>? trace, ref string last)
    {
        if (trace == null) return;
        string text = trace.Count == 0 ? "none found" : string.Join("\n  ", trace);
        if (text == last) return;
        last = text;
        Log.Write($"{title}:\n  {text}");
    }

    public void Dispose() => _timer.Dispose();
}
