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

    private static List<ProcInfo>? Top(List<(ProcSample P, double V)> list, Func<double, string> fmt)
    {
        if (list.Count == 0) return null;
        return list.OrderByDescending(x => x.V).Take(TopCount)
            .Select(x => new ProcInfo(x.P.Pid, x.P.Name, x.V, fmt(x.V))).ToList();
    }

    /// <summary>A fresh process list for tree operations.</summary>
    public static List<ProcEntry> Snapshot()
        => SysInfo.Processes()?.Select(p => new ProcEntry(p.Pid, p.ParentPid, p.CreateTime)).ToList() ?? new();
}
