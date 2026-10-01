using Ember.App;
using Ember.Core.Config;
using Ember.Core.Net;
using Ember.Core.Status;
using Ember.Core.Util;
using Ember.Native;

namespace Ember.Sensors;

/// <summary>
/// Reads all sensors on one background thread, at the sensor interval.
/// Each source that fails gives null in the snapshot (its items hide, no error is shown).
/// </summary>
internal sealed partial class Sampler : IDisposable
{
    private readonly Func<EmberSettings> _settings;
    private readonly Func<Needs> _needs;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _running = new(true);
    private readonly AutoResetEvent _wake = new(false);
    private volatile bool _stop;

    private readonly PdhQuery _pdh = new();
    private readonly TempSource _temps = new();
    private readonly NetRates _net = new();
    private readonly ProcessTracker _procs = new();

    /// <summary>Called on the sampler thread after each sample.</summary>
    public event Action<Snapshot>? Sampled;

    /// <summary>Extra data added to each snapshot (Claude lines, network quality).</summary>
    public Func<Snapshot, Snapshot>? Decorate { get; set; }

    public Sampler(Func<EmberSettings> settings, Func<Needs> needs)
    {
        _settings = settings;
        _needs = needs;
        _thread = new Thread(Loop) { IsBackground = true, Name = "Ember.Sampler", Priority = ThreadPriority.BelowNormal };
    }

    public void Start() => _thread.Start();

    /// <summary>Stop sampling (session locked or display off).</summary>
    public void Pause() => _running.Reset();

    public void Resume()
    {
        _running.Set();
        _wake.Set();
    }

    /// <summary>Sample now (for example after a settings change).</summary>
    public void Poke() => _wake.Set();

    private void Loop()
    {
        InitCpu();
        while (!_stop)
        {
            _running.Wait();
            if (_stop) break;
            var start = DateTime.UtcNow;
            try
            {
                var snap = Sample();
                if (Decorate != null) snap = Decorate(snap);
                Sampled?.Invoke(snap);
            }
            catch (Exception ex)
            {
                Log.Write("sample failed: " + ex);
            }
            double interval = Math.Clamp(_settings().Intervals.SensorSeconds, 0.25, 60);
            var wait = TimeSpan.FromSeconds(interval) - (DateTime.UtcNow - start);
            if (wait > TimeSpan.Zero) _wake.WaitOne(wait);
            if (!_running.IsSet) ResetRates();
        }
    }

    private void ResetRates()
    {
        _net.Reset();
        _procs.Reset();
    }

    private Snapshot Sample()
    {
        var cfg = _settings();
        var needs = _needs();
        var now = DateTime.Now;

        UpdateCounters(cfg);
        bool pdhOk = _pdh.Collect();
        if (!pdhOk) Log.SourceFailed("pdh.collect"); else Log.SourceOk("pdh.collect");

        bool cpuTemp = cfg.PreviewAll || (cfg.Fields.CpuTemp && cfg.IsOn("cpu.load")) || (cfg.Fields.CoreDetails && cfg.IsOn("cpu.cores"));
        bool gpuTemp = cfg.Fields.GpuTemp && AnyGpuItem(cfg);
        _temps.Update(cpuTemp, gpuTemp);

        var cpu = pdhOk ? ReadCpu(cfg) : null;
        var gpus = pdhOk ? ReadGpus(cfg) : null;

        // Process scan only when a top-process line needs it.
        bool scan = needs.TopCpu || needs.TopMem || needs.TopDisk;
        List<ProcInfo>? topCpu = null, topMem = null, topDisk = null;
        if (scan)
        {
            if (_procs.Sample(now))
            {
                if (needs.TopCpu) topCpu = _procs.TopCpu();
                if (needs.TopMem) topMem = _procs.TopMemory();
                if (needs.TopDisk) topDisk = _procs.TopDisk();
            }
        }
        else
        {
            _procs.Reset();
        }

        return new Snapshot
        {
            Time = now,
            Cpu = cpu,
            Net = cfg.IsOn("net.down") || cfg.IsOn("net.up") || cfg.IsOn("net.adapters") ? ReadNet(cfg, now) : null,
            Gpus = gpus,
            Mem = ReadMemory(cfg, pdhOk),
            Disks = pdhOk ? ReadDisks(cfg) : null,
            Volumes = cfg.IsOn("disk.free") ? ReadVolumes() : null,
            TopCpu = topCpu,
            TopMem = topMem,
            TopDisk = topDisk,
        };
    }

    /// <summary>Adds or removes optional PDH counters when settings change.</summary>
    private void UpdateCounters(EmberSettings cfg)
    {
        UpdateGpuCounters(cfg);
        UpdateMemDiskCounters(cfg);
    }

    // ───────── CPU ─────────

    private PdhQuery.PdhCounter? _cpuTotal, _cpuCores, _cpuPerf, _corePerf, _coreFreq;
    private Dictionary<(int, int), (int Efficiency, int Core)> _cpuSets = new();

    private void InitCpu()
    {
        _cpuTotal = _pdh.Add(@"\Processor Information(_Total)\% Processor Utility");
        _cpuCores = _pdh.Add(@"\Processor Information(*)\% Processor Utility");
        _cpuPerf = _pdh.Add(@"\Processor Information(_Total)\% Processor Performance");
        // Effective clock = base frequency × % Processor Performance (also per logical core).
        _corePerf = _pdh.Add(@"\Processor Information(*)\% Processor Performance");
        _coreFreq = _pdh.Add(@"\Processor Information(*)\Processor Frequency");
        if (_cpuTotal == null) Log.SourceFailed("pdh.cpu");
        _cpuSets = SysInfo.CpuSets();
        if (_cpuSets.Values.Select(v => v.Efficiency).Distinct().Count() > 1) Log.Write("hybrid CPU: " + _cpuSets.Values.Select(v => v.Efficiency).Distinct().Count() + " core types");
    }

    private static List<(int Group, int Number, double Value)> PerCore(List<(string Instance, double Value)>? arr)
    {
        var list = new List<(int Group, int Number, double Value)>();
        if (arr == null) return list;
        foreach (var (inst, v) in arr)
            if (CpuNames.Parse(inst) is { } gn) list.Add((gn.Group, gn.Number, v));
        list.Sort((a, b) => a.Group != b.Group ? a.Group.CompareTo(b.Group) : a.Number.CompareTo(b.Number));
        return list;
    }

    private CpuData? ReadCpu(EmberSettings cfg)
    {
        if (_cpuTotal?.Value() is not { } load) return null;
        double[]? cores = null, clocks = null, temps = null;
        int[]? groups = null;
        bool wantCores = cfg.IsOn("cpu.cores") || cfg.IsOn("cpu.top") || cfg.PreviewAll;
        var perfAll = _corePerf?.Array();
        var freqAll = _coreFreq?.Array();
        double? clock = null;
        if (perfAll != null && freqAll != null)
        {
            var perfTotal = perfAll.FirstOrDefault(x => x.Instance == "_Total");
            var freqTotal = freqAll.FirstOrDefault(x => x.Instance == "_Total");
            if (perfTotal.Instance != null && freqTotal.Instance != null && freqTotal.Value > 0) clock = freqTotal.Value * perfTotal.Value / 100;
        }
        if (wantCores && _cpuCores?.Array() is { } arr)
        {
            var list = PerCore(arr);
            cores = list.Select(x => x.Value).ToArray();
            bool hybrid = _cpuSets.Values.Select(v => v.Efficiency).Distinct().Count() > 1;
            groups = hybrid ? list.Select(x => _cpuSets.GetValueOrDefault((x.Group, x.Number)).Efficiency).ToArray() : null;
            if (cfg.Fields.CoreDetails || cfg.PreviewAll)
            {
                var perf = PerCore(perfAll).ToDictionary(x => (x.Group, x.Number), x => x.Value);
                var freq = PerCore(freqAll).ToDictionary(x => (x.Group, x.Number), x => x.Value);
                clocks = list.Select(x => freq.TryGetValue((x.Group, x.Number), out var f) && perf.TryGetValue((x.Group, x.Number), out var pf) ? f * pf / 100 : 0).ToArray();
                temps = CoreTemps(list);
            }
        }
        return new CpuData
        {
            Load = load,
            CoreLoads = cores,
            CoreGroups = groups,
            Temp = _temps.CpuPackage,
            Perf = cfg.IsOn("cpu.throttle") || cfg.PreviewAll ? _cpuPerf?.Value() : null,
            ClockMHz = clock,
            CoreClocksMHz = clocks,
            CoreTemps = temps,
        };
    }

    /// <summary>
    /// Temperature of each logical core: LHM gives one per physical core ("CPU Core #n", or "P-Core #n"
    /// then "E-Core #n"). Physical cores are ordered by their CPU-set core index (P-cores first on Intel hybrid).
    /// Unknown → NaN (for example AMD, where LHM has no per-core temperature).
    /// </summary>
    private double[]? CoreTemps(List<(int Group, int Number, double Value)> logical)
    {
        var lhm = _temps.CoreTemps;
        if (lhm.Count == 0) return null;
        var ordered = lhm.Where(t => t.Name.StartsWith("CPU Core", StringComparison.Ordinal)).ToList();
        if (ordered.Count == 0)
            ordered = lhm.Where(t => t.Name.StartsWith("P-", StringComparison.Ordinal)).Concat(lhm.Where(t => t.Name.StartsWith("E-", StringComparison.Ordinal))).ToList();
        ordered = ordered.OrderBy(t => t.Name.StartsWith("E-", StringComparison.Ordinal) ? 1 : 0).ThenBy(t => CoreNumber(t.Name)).ToList();
        var physical = logical.Select(x => _cpuSets.TryGetValue((x.Group, x.Number), out var cs) ? cs.Core : -1).ToArray();
        var order = physical.Where(c => c >= 0).Distinct().OrderBy(c => c).ToList();
        return physical.Select(c =>
        {
            int k = order.IndexOf(c);
            return k >= 0 && k < ordered.Count ? ordered[k].Celsius : double.NaN;
        }).ToArray();
    }

    private static int CoreNumber(string name)
    {
        int i = name.LastIndexOf('#');
        return i >= 0 && int.TryParse(name[(i + 1)..], out int n) ? n : 0;
    }

    // ───────── Network ─────────

    private List<AdapterRate>? ReadNet(EmberSettings cfg, DateTime now)
    {
        var rows = SysInfo.Interfaces();
        if (rows == null)
        {
            Log.SourceFailed("net.ifTable");
            return null;
        }
        Log.SourceOk("net.ifTable");
        var excluded = cfg.Network.ExcludedAdapters;
        var physical = rows.Where(r => AdapterFilter.IsPhysical(r, excluded)).ToList();
        var rates = _net.Update(physical, now);
        return _net.HasBaseline ? rates : null;
    }

    public void Dispose()
    {
        _stop = true;
        _running.Set();
        _wake.Set();
        bool ended = !_thread.IsAlive || _thread.Join(3000);
        if (ended)
        {
            // Never close the query or LHM while the thread may still use them.
            _pdh.Dispose();
            _temps.Dispose();
        }
    }
}
