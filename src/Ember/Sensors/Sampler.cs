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

        bool cpuTemp = cfg.Items.CpuTemp;
        bool gpuTemp = cfg.Items.GpuTemp && AnyGpuItem(cfg);
        _temps.Update(cpuTemp, gpuTemp);

        var cpu = pdhOk ? ReadCpu(cfg) : null;
        var gpus = pdhOk ? ReadGpus(cfg) : null;

        // Process scan only when a top-process line needs it.
        bool scan = needs.TopCpu || needs.TopMem || needs.TopDisk;
        ProcInfo? topCpu = null, topMem = null, topDisk = null;
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
            Net = cfg.Items.Network ? ReadNet(cfg, now) : null,
            Gpus = gpus,
            Mem = ReadMemory(cfg, pdhOk),
            Disks = pdhOk ? ReadDisks(cfg) : null,
            Volumes = cfg.Items.DiskFree ? ReadVolumes() : null,
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

    private PdhQuery.PdhCounter? _cpuTotal, _cpuCores, _cpuPerf;
    private Dictionary<(int, int), int> _effClass = new();

    private void InitCpu()
    {
        _cpuTotal = _pdh.Add(@"\Processor Information(_Total)\% Processor Utility");
        _cpuCores = _pdh.Add(@"\Processor Information(*)\% Processor Utility");
        _cpuPerf = _pdh.Add(@"\Processor Information(_Total)\% Processor Performance");
        if (_cpuTotal == null) Log.SourceFailed("pdh.cpu");
        _effClass = SysInfo.CpuEfficiencyClasses();
        if (_effClass.Values.Distinct().Count() > 1) Log.Write("hybrid CPU: " + _effClass.Values.Distinct().Count() + " core types");
    }

    private CpuData? ReadCpu(EmberSettings cfg)
    {
        if (_cpuTotal?.Value() is not { } load) return null;
        double[]? cores = null;
        int[]? groups = null;
        if (cfg.Items.CpuCores && _cpuCores?.Array() is { } arr)
        {
            var list = new List<(int Group, int Number, double Value)>();
            foreach (var (inst, v) in arr)
                if (CpuNames.Parse(inst) is { } gn) list.Add((gn.Group, gn.Number, v));
            list.Sort((a, b) => a.Group != b.Group ? a.Group.CompareTo(b.Group) : a.Number.CompareTo(b.Number));
            cores = list.Select(x => x.Value).ToArray();
            bool hybrid = _effClass.Values.Distinct().Count() > 1;
            groups = hybrid ? list.Select(x => _effClass.GetValueOrDefault((x.Group, x.Number))).ToArray() : null;
        }
        return new CpuData
        {
            Load = load,
            CoreLoads = cores,
            CoreGroups = groups,
            Temp = _temps.CpuPackage,
            Perf = cfg.Items.CpuThrottle ? _cpuPerf?.Value() : null,
        };
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
        _thread.Join(2000);
        _pdh.Dispose();
        _temps.Dispose();
    }
}
