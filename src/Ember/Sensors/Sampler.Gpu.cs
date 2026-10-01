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

    /// <summary>GPU load of each process (busiest engine), from the last sample. Null if not measured.</summary>
    public volatile Dictionary<int, double>? GpuByPid;

    /// <summary>For each adapter (LUID) and process: the load on its busiest engine.</summary>
    private static Dictionary<(string Luid, int Pid), double> PerProcess(List<(string Instance, double Value)> engines)
    {
        var perEngine = new Dictionary<(string Luid, int Pid, string Engine), double>();
        foreach (var (inst, v) in engines)
            if (GpuNames.TryParseEngine(inst, out var e) && e.Pid > 0)
                perEngine[(e.Luid, e.Pid, e.EngineKey)] = perEngine.GetValueOrDefault((e.Luid, e.Pid, e.EngineKey)) + v;
        var result = new Dictionary<(string, int), double>();
        foreach (var ((luid, pid, _), v) in perEngine)
            result[(luid, pid)] = Math.Max(result.GetValueOrDefault((luid, pid)), Math.Min(100, v));
        return result;
    }

    private static bool AnyGpuItem(EmberSettings cfg)
        => cfg.IsOn("gpu.load") || cfg.IsOn("gpu.vram") || cfg.IsOn("gpu.top") || cfg.PreviewAll;

    private void UpdateGpuCounters(EmberSettings cfg)
    {
        bool engine = cfg.IsOn("gpu.load") || cfg.IsOn("gpu.top") || cfg.PreviewAll;
        if (engine && _gpuEngine == null) _gpuEngine = _pdh.Add(@"\GPU Engine(*)\Utilization Percentage");
        else if (!engine && _gpuEngine != null) { _pdh.Remove(_gpuEngine); _gpuEngine = null; }

        bool mem = cfg.IsOn("gpu.vram") || cfg.PreviewAll;
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
        Dictionary<(string Luid, int Pid), double>? perProcess = null;
        if (_gpuEngine?.Array() is { } engines)
        {
            loads = GpuAggregator.Aggregate(engines);
            perProcess = PerProcess(engines);
            var byPid = new Dictionary<int, double>();
            foreach (var ((_, pid), v) in perProcess) byPid[pid] = Math.Max(byPid.GetValueOrDefault(pid), v);
            GpuByPid = byPid;
        }
        else GpuByPid = null;

        Dictionary<string, double>? vram = null;
        if (_gpuMem?.Array() is { } mem)
        {
            vram = new();
            foreach (var (inst, v) in mem)
                if (GpuNames.AdapterLuid(inst) is { } luid) vram[luid] = vram.GetValueOrDefault(luid) + v;
        }

        var result = new List<GpuData>();
        foreach (var a in GpuDedupe.Select(_adapters, a => (a.Name, a.VendorId, a.DeviceId, a.SubSysId, a.DedicatedVideoMemory),
                     a => (loads?.GetValueOrDefault(a.Luid)?.Load ?? 0) > 0 || (vram?.GetValueOrDefault(a.Luid) ?? 0) > 0))
        {
            GpuAdapterLoad? load = loads?.GetValueOrDefault(a.Luid);
            // No engine instance for an adapter with a counter = idle (0 %).
            double? loadValue = loads == null ? null : load?.Load ?? 0;
            // Top processes (names only while the GPU is busy enough to show them; names cost a process open each).
            List<ProcInfo>? tops = null;
            if (perProcess != null && (cfg.IsOn("gpu.top") || cfg.PreviewAll) && (load?.Load ?? 0) >= cfg.Thresholds.GpuTopProcessLoad * 0.5)
                tops = perProcess.Where(kv => kv.Key.Luid == a.Luid && kv.Value > 0).OrderByDescending(kv => kv.Value).Take(5)
                    .Select(kv => new ProcInfo(kv.Key.Pid, PidName(kv.Key.Pid), kv.Value, Core.Render.Format.Pct4(kv.Value).Trim())).ToList();
            result.Add(new GpuData
            {
                Luid = a.Luid,
                Name = a.Name,
                Integrated = a.Integrated,
                Load = loadValue,
                Temp = MatchTemp(a.Name),
                VramUsedBytes = vram?.GetValueOrDefault(a.Luid),
                VramTotalBytes = a.DedicatedVideoMemory > 0 ? a.DedicatedVideoMemory : null,
                Tops = tops,
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
        string name = ProcessInfo.Name(pid) is { } n ? ProcessTracker.DisplayName(pid, n) : "pid " + pid;
        if (_pidNames.Count > 256) _pidNames.Clear();
        _pidNames[pid] = (name, now);
        return name;
    }
}
