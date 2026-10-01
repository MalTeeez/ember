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

    /// <summary>Highest CPU % of the whole machine (100 % = all logical cores).</summary>
    public ProcInfo? TopCpu()
    {
        if (!_hasPrev) return null;
        ProcSample? best = null;
        double bestCpu = 0;
        foreach (var (key, p) in _cur)
        {
            if (Skip(p) || !_prev.TryGetValue(key, out var old)) continue;
            double cpu = (p.CpuTime - old.CpuTime) / 1e7 / Seconds / Cpus * 100;
            if (cpu > bestCpu) { bestCpu = cpu; best = p; }
        }
        return best is { } b ? new ProcInfo(b.Pid, b.Name, Format.Percent(bestCpu, 1).Trim()) : null;
    }

    /// <summary>Highest private bytes.</summary>
    public ProcInfo? TopMemory()
    {
        ProcSample? best = null;
        foreach (var p in _cur.Values)
            if (!Skip(p) && (best == null || p.PrivateBytes > best.Value.PrivateBytes)) best = p;
        return best is { } b ? new ProcInfo(b.Pid, b.Name, Format.Bytes(b.PrivateBytes)) : null;
    }

    /// <summary>Highest read+write bytes/s (also counts network I/O, so approximate).</summary>
    public ProcInfo? TopDisk()
    {
        if (!_hasPrev) return null;
        ProcSample? best = null;
        double bestRate = 0;
        foreach (var (key, p) in _cur)
        {
            if (Skip(p) || !_prev.TryGetValue(key, out var old)) continue;
            double rate = p.IoBytes >= old.IoBytes ? (p.IoBytes - old.IoBytes) / Seconds : 0;
            if (rate > bestRate) { bestRate = rate; best = p; }
        }
        return best is { } b ? new ProcInfo(b.Pid, b.Name, Format.BytesPerSecond(bestRate)) : null;
    }

    /// <summary>A fresh process list for tree operations.</summary>
    public static List<ProcEntry> Snapshot()
        => SysInfo.Processes()?.Select(p => new ProcEntry(p.Pid, p.ParentPid, p.CreateTime)).ToList() ?? new();
}
