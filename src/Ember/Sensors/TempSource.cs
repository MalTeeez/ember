#if LHM
using LibreHardwareMonitor.Hardware;
#endif
using Ember.App;

namespace Ember.Sensors;

/// <summary>
/// Temperatures from LibreHardwareMonitorLib (LHM). LHM loads a kernel driver; this needs admin rights.
/// Without the driver (or in an EmberOffline build) all temperatures are null and their items stay hidden.
/// The LHM Computer is opened on a background thread on first use.
/// </summary>
internal sealed class TempSource : IDisposable
{
    public readonly record struct GpuTemp(string Name, double Celsius);

#if LHM
    private Computer? _computer;
    private volatile bool _opening, _opened, _failed;
    private bool _cpuEnabled, _gpuEnabled;
#endif

    public double? CpuPackage { get; private set; }
    public List<GpuTemp> Gpus { get; } = new();

    /// <summary>Reads the temperatures. Only the enabled hardware is updated.</summary>
    public void Update(bool cpu, bool gpu)
    {
        CpuPackage = null;
        Gpus.Clear();
#if LHM
        if (!cpu && !gpu) return;
        if (_failed) return;
        if (!_opened)
        {
            if (!_opening)
            {
                _opening = true;
                var t = new Thread(Open) { IsBackground = true, Name = "Ember.LHM" };
                t.Start();
            }
            return;
        }
        try
        {
            if (_computer == null) return;
            if (_cpuEnabled != cpu) _computer.IsCpuEnabled = _cpuEnabled = cpu;
            if (_gpuEnabled != gpu) _computer.IsGpuEnabled = _gpuEnabled = gpu;
            foreach (var hw in _computer.Hardware)
            {
                bool isCpu = hw.HardwareType == HardwareType.Cpu;
                bool isGpu = hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;
                if (!(isCpu && cpu) && !(isGpu && gpu)) continue;
                hw.Update();
                if (isCpu)
                {
                    var t = PackageTemp(hw);
                    if (t != null) CpuPackage = CpuPackage == null ? t : Math.Max(CpuPackage.Value, t.Value);
                }
                else
                {
                    var t = GpuCoreTemp(hw);
                    if (t != null) Gpus.Add(new GpuTemp(hw.Name, t.Value));
                }
            }
            Log.SourceOk("lhm");
        }
        catch (Exception ex)
        {
            Log.SourceFailed("lhm", ex);
        }
#endif
    }

#if LHM
    private void Open()
    {
        try
        {
            var c = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
            c.Open();
            _cpuEnabled = _gpuEnabled = true;
            _computer = c;
            _opened = true;
            Log.Write("LHM opened: " + string.Join(", ", c.Hardware.Select(h => $"{h.HardwareType}:{h.Name}")));
        }
        catch (Exception ex)
        {
            _failed = true;
            Log.SourceFailed("lhm.open", ex);
        }
        finally { _opening = false; }
    }

    /// <summary>
    /// Intel: "CPU Package". AMD Ryzen: "Core (Tctl/Tdie)" or "Core (Tctl)" (package, not per core).
    /// Fallback: the highest temperature sensor of the CPU.
    /// </summary>
    private static double? PackageTemp(IHardware hw)
    {
        var temps = hw.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue).ToList();
        foreach (var name in new[] { "CPU Package", "Core (Tctl/Tdie)", "Core (Tctl)", "Package", "Core (Tdie)" })
        {
            var s = temps.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (s != null) return s.Value;
        }
        var max = temps.Where(t => !t.Name.Contains("Distance", StringComparison.OrdinalIgnoreCase)).Select(t => t.Value!.Value).DefaultIfEmpty(float.NaN).Max();
        return float.IsNaN(max) ? null : max;
    }

    private static double? GpuCoreTemp(IHardware hw)
    {
        var temps = hw.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue).ToList();
        var core = temps.FirstOrDefault(s => s.Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase)) ?? temps.FirstOrDefault();
        return core?.Value;
    }
#endif

    public void Dispose()
    {
#if LHM
        try { _computer?.Close(); } catch { }
        _computer = null;
#endif
    }
}
