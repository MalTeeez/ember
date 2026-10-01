using Ember.Core.Render;
using Ember.Core.Status;
using Ember.Core.Util;
using Ember.Native;

namespace Ember.Sensors;

/// <summary>
/// Per-process CPU time, private bytes and I/O bytes from one NtQuerySystemInformation call.
/// CPU % and I/O rate are the difference between two samples.
/// </summary>
internal sealed class ProcessTracker
{
    private Dictionary<(int Pid, long Create), ProcSample> _prev = new();
    private Dictionary<(int Pid, long Create), ProcSample> _cur = new();
    private DateTime _prevTime, _curTime;
    private bool _hasPrev;
    private static readonly int Cpus = Environment.ProcessorCount;

    /// <summary>The latest full list (for the process tree).</summary>
    public IReadOnlyCollection<ProcSample> Current => _cur.Values;

    public bool Sample(DateTime now)
    {
        var list = SysInfo.Processes();
        if (list == null)
        {
            App.Log.SourceFailed("processes");
            return false;
        }
        App.Log.SourceOk("processes");
        (_prev, _cur) = (_cur, _prev);
        _prevTime = _curTime;
        _hasPrev = _prev.Count > 0;
        _cur.Clear();
        foreach (var p in list) _cur[(p.Pid, p.CreateTime)] = p;
        _curTime = now;
        return true;
    }

    public void Reset()
    {
        _prev.Clear();
        _cur.Clear();
        _hasPrev = false;
    }

    private double Seconds => Math.Max(0.001, (_curTime - _prevTime).TotalSeconds);

    private static bool Skip(ProcSample p) => p.Pid <= 4 || p.Name is "Idle" or "System" or "Memory Compression" or "Registry";

    private const int TopCount = 5;

    /// <summary>Highest CPU % of the whole machine (100 % = all logical cores), highest first.</summary>
    public List<ProcInfo>? TopCpu()
    {
        if (!_hasPrev) return null;
        var list = new List<(ProcSample P, double V)>();
        foreach (var (key, p) in _cur)
        {
            if (Skip(p) || !_prev.TryGetValue(key, out var old)) continue;
            list.Add((p, (p.CpuTime - old.CpuTime) / 1e7 / Seconds / Cpus * 100));
        }
        return Top(list, v => Format.Pct4(v).Trim());
    }

    /// <summary>Highest private bytes, highest first.</summary>
    public List<ProcInfo>? TopMemory()
        => Top(_cur.Values.Where(p => !Skip(p)).Select(p => (p, (double)p.PrivateBytes)).ToList(), v => Format.Bytes4(v).Trim());

    /// <summary>Highest read+write bytes/s (also counts network I/O, so approximate), highest first.</summary>
    public List<ProcInfo>? TopDisk()
    {
        if (!_hasPrev) return null;
        var list = new List<(ProcSample P, double V)>();
        foreach (var (key, p) in _cur)
        {
            if (Skip(p) || !_prev.TryGetValue(key, out var old)) continue;
            list.Add((p, p.IoBytes >= old.IoBytes ? (p.IoBytes - old.IoBytes) / Seconds : 0));
        }
        return Top(list, v => Format.Bytes4(v).Trim() + "/s");
    }

    /// <summary>
    /// Page faults per process since the last sample: hard (at least 1/s) or soft (at least 100/s),
    /// and the totals of all processes. Display names only for the 12 busiest of each kind.
    /// </summary>
    public (List<FaultProc>? List, double Hard, double Soft) Faults()
    {
        if (!_hasPrev) return (null, 0, 0);
        var raw = new List<(ProcSample P, double Hard, double Soft)>();
        double hardTotal = 0, softTotal = 0;
        foreach (var (key, p) in _cur)
        {
            if (p.Pid <= 0 || !_prev.TryGetValue(key, out var old) || p.HardFaults < old.HardFaults || p.PageFaults < old.PageFaults) continue;
            double hard = (p.HardFaults - old.HardFaults) / Seconds;
            double soft = Math.Max(0, (p.PageFaults - old.PageFaults) / Seconds - hard);
            hardTotal += hard;
            softTotal += soft;
            if (hard >= 1 || soft >= 100) raw.Add((p, hard, soft));
        }
        var keep = raw.OrderByDescending(x => x.Hard).Take(12).Concat(raw.OrderByDescending(x => x.Soft).Take(12)).Distinct();
        var list = keep.Select(x => new FaultProc(x.P.Pid, Display(x.P), x.P.PrivateBytes, x.Hard, x.Soft)).ToList();
        return (list, hardTotal, softTotal);
    }

    private List<ProcInfo>? Top(List<(ProcSample P, double V)> list, Func<double, string> fmt)
    {
        if (list.Count == 0) return null;
        return list.OrderByDescending(x => x.V).Take(TopCount)
            .Select(x => new ProcInfo(x.P.Pid, Display(x.P), x.V, fmt(x.V))).ToList();
    }

    private readonly Dictionary<(int Pid, long Create), string> _display = new();

    /// <summary>"python3.13 main.py": name without .exe and the first argument (command line read once per process).</summary>
    private string Display(ProcSample p)
    {
        var key = (p.Pid, p.CreateTime);
        if (_display.TryGetValue(key, out var d)) return d;
        if (_display.Count > 512) _display.Clear();
        return _display[key] = DisplayName(p.Pid, p.Name);
    }

    /// <summary>Display name of a process (no cache).</summary>
    public static string DisplayName(int pid, string name)
        => ProcessNames.Display(name, pid > 4 ? ProcessInfo.CommandLine(pid) : null);

    /// <summary>A fresh process list for tree operations.</summary>
    public static List<ProcEntry> Snapshot()
        => SysInfo.Processes()?.Select(p => new ProcEntry(p.Pid, p.ParentPid, p.CreateTime)).ToList() ?? new();
}
