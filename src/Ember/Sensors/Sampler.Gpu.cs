using Ember.App;
using Ember.Core.Config;
using Ember.Core.Gpu;
using Ember.Core.Status;
using Ember.Native;

namespace Ember.Sensors;

// GPU: PDH "GPU Engine" (load, top process), PDH "GPU Adapter Memory" (VRAM use),
// DXGI (names, LUIDs, VRAM total), LHM (temperature).
internal sealed partial class Sampler
{
    private PdhQuery.PdhCounter? _gpuEngine, _gpuMem;
    private List<DxgiAdapter>? _adapters;
    private DateTime _adaptersTime;
    private readonly Dictionary<int, (string Name, DateTime Time)> _pidNames = new();

    private static bool AnyGpuItem(EmberSettings cfg)
        => cfg.Items.GpuLoad || cfg.Items.GpuTemp || cfg.Items.GpuVram || cfg.Items.GpuTopProcess;

    private void UpdateGpuCounters(EmberSettings cfg)
    {
        bool engine = cfg.Items.GpuLoad || cfg.Items.GpuTopProcess;
        if (engine && _gpuEngine == null) _gpuEngine = _pdh.Add(@"\GPU Engine(*)\Utilization Percentage");
        else if (!engine && _gpuEngine != null) { _pdh.Remove(_gpuEngine); _gpuEngine = null; }

        bool mem = cfg.Items.GpuVram;
        if (mem && _gpuMem == null) _gpuMem = _pdh.Add(@"\GPU Adapter Memory(*)\Dedicated Usage");
        else if (!mem && _gpuMem != null) { _pdh.Remove(_gpuMem); _gpuMem = null; }
    }

    private List<GpuData>? ReadGpus(EmberSettings cfg)
    {
        if (!AnyGpuItem(cfg)) return null;

        // Adapter list: at start and every 10 min (driver updates, eGPU).
        if (_adapters == null || DateTime.UtcNow - _adaptersTime > TimeSpan.FromMinutes(10))
        {
            try
            {
                _adapters = Dxgi.Adapters();
                _adaptersTime = DateTime.UtcNow;
                Log.SourceOk("dxgi");
            }
            catch (Exception ex)
            {
                Log.SourceFailed("dxgi", ex);
                return null;
            }
        }
        if (_adapters.Count == 0) return null;

        Dictionary<string, GpuAdapterLoad>? loads = null;
        if (_gpuEngine?.Array() is { } engines) loads = GpuAggregator.Aggregate(engines);

        Dictionary<string, double>? vram = null;
        if (_gpuMem?.Array() is { } mem)
        {
            vram = new();
            foreach (var (inst, v) in mem)
                if (GpuNames.AdapterLuid(inst) is { } luid) vram[luid] = vram.GetValueOrDefault(luid) + v;
        }

        var result = new List<GpuData>();
        foreach (var a in _adapters)
        {
            GpuAdapterLoad? load = loads?.GetValueOrDefault(a.Luid);
            // No engine instance for an adapter with a counter = idle (0 %).
            double? loadValue = loads == null ? null : load?.Load ?? 0;
            ProcInfo? top = null;
            if (load is { TopPid: > 0 } l && cfg.Items.GpuTopProcess && l.Load >= cfg.Thresholds.GpuTopProcessLoad * 0.5)
                top = new ProcInfo(l.TopPid, PidName(l.TopPid), Core.Render.Format.Percent(l.TopPidLoad, 1).Trim());
            result.Add(new GpuData
            {
                Luid = a.Luid,
                Name = a.Name,
                Integrated = a.Integrated,
                Load = loadValue,
                Temp = MatchTemp(a.Name),
                VramUsedBytes = vram?.GetValueOrDefault(a.Luid),
                VramTotalBytes = a.DedicatedVideoMemory > 0 ? a.DedicatedVideoMemory : null,
                Top = top,
                TopLoad = load?.TopPidLoad ?? 0,
            });
        }
        return result;
    }

    /// <summary>LHM GPU temperature for a DXGI adapter, matched by name.</summary>
    private double? MatchTemp(string adapterName)
    {
        foreach (var t in _temps.Gpus)
        {
            if (t.Name.Contains(adapterName, StringComparison.OrdinalIgnoreCase) ||
                adapterName.Contains(t.Name, StringComparison.OrdinalIgnoreCase))
                return t.Celsius;
        }
        // One GPU with a temperature and one discrete adapter: use it.
        return _temps.Gpus.Count == 1 && _adapters!.Count(a => !a.Integrated) == 1 && !_adapters!.First(a => a.Name == adapterName).Integrated
            ? _temps.Gpus[0].Celsius
            : null;
    }

    private string PidName(int pid)
    {
        var now = DateTime.UtcNow;
        if (_pidNames.TryGetValue(pid, out var e) && now - e.Time < TimeSpan.FromSeconds(30)) return e.Name;
        string name = ProcessInfo.Name(pid) ?? ("pid " + pid);
        if (_pidNames.Count > 256) _pidNames.Clear();
        _pidNames[pid] = (name, now);
        return name;
    }
}
