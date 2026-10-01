using Ember.App;
using Ember.Core.Config;
using Ember.Core.Status;
using Ember.Core.Util;
using Ember.Native;

namespace Ember.Sensors;

// Memory: GlobalMemoryStatusEx, GetPerformanceInfo, PDH Memory\Pages Input/sec.
// Disk: PDH PhysicalDisk(*) % Idle Time and Avg. Disk Queue Length, GetDiskFreeSpaceEx.
internal sealed partial class Sampler
{
    private PdhQuery.PdhCounter? _pagesIn, _diskIdle, _diskQueue;

    private void UpdateMemDiskCounters(EmberSettings cfg)
    {
        bool pf = cfg.IsOn("mem.pagein") || cfg.PreviewAll;
        if (pf && _pagesIn == null) _pagesIn = _pdh.Add(@"\Memory\Pages Input/sec");
        else if (!pf && _pagesIn != null) { _pdh.Remove(_pagesIn); _pagesIn = null; }

        bool disk = cfg.IsOn("disk.busy") || cfg.PreviewAll;
        if (disk && _diskIdle == null)
        {
            _diskIdle = _pdh.Add(@"\PhysicalDisk(*)\% Idle Time");
            _diskQueue = _pdh.Add(@"\PhysicalDisk(*)\Avg. Disk Queue Length");
        }
        else if (!disk && _diskIdle != null)
        {
            _pdh.Remove(_diskIdle);
            _pdh.Remove(_diskQueue);
            _diskIdle = _diskQueue = null;
        }
    }

    private MemData? ReadMemory(EmberSettings cfg, bool pdhOk)
    {
        if (!(cfg.IsOn("mem.avail") || cfg.IsOn("mem.commit") || cfg.IsOn("mem.pagein") || cfg.PreviewAll)) return null;
        if (!SysInfo.Memory(out ulong avail, out ulong total))
        {
            Log.SourceFailed("memory");
            return null;
        }
        SysInfo.Commit(out ulong commit, out ulong limit); // zero on failure: commit item hides
        return new MemData
        {
            AvailBytes = avail,
            TotalBytes = total,
            CommitBytes = commit,
            CommitLimitBytes = limit,
            PagesInPerSec = pdhOk ? _pagesIn?.Value() : null,
        };
    }

    private List<DiskData>? ReadDisks(EmberSettings cfg)
    {
        if (_diskIdle?.Array() is not { } idle) return null;
        var queue = _diskQueue?.Array()?.ToDictionary(x => x.Instance, x => x.Value) ?? new();
        var list = new List<DiskData>();
        foreach (var (inst, idleValue) in idle)
        {
            if (DiskNames.Parse(inst) is not { } n) continue;
            double active = Math.Clamp(100 - idleValue, 0, 100);
            list.Add(new DiskData(n.Label, n.Volumes, active, queue.GetValueOrDefault(inst)));
        }
        return list;
    }

    private DateTime _volumesTime;
    private List<VolumeData>? _volumes;

    /// <summary>Free space of fixed volumes. Read every 10 s (it changes slowly).</summary>
    private List<VolumeData>? ReadVolumes()
    {
        if (_volumes != null && DateTime.UtcNow - _volumesTime < TimeSpan.FromSeconds(10)) return _volumes;
        try
        {
            _volumes = SysInfo.FixedVolumes().Select(v => new VolumeData(v.Root, v.Free, v.Total)).ToList();
            _volumesTime = DateTime.UtcNow;
            return _volumes;
        }
        catch (Exception ex)
        {
            Log.SourceFailed("volumes", ex);
            return null;
        }
    }
}
