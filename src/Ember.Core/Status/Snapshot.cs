using Ember.Core.Claude;
using Ember.Core.Net;

namespace Ember.Core.Status;

/// <summary>A process for a "└ top process" line.</summary>
public sealed record ProcInfo(int Pid, string Name, double Value, string ValueText);

public sealed class CpuData
{
    /// <summary>Combined load, % (may be above 100 with turbo; display is limited to 100).</summary>
    public double Load { get; set; }
    /// <summary>Raw load for each logical core, %.</summary>
    public double[]? CoreLoads { get; set; }
    /// <summary>Core group (efficiency class) for each logical core. Null = one group.</summary>
    public int[]? CoreGroups { get; set; }
    /// <summary>Package temperature, °C.</summary>
    public double? Temp { get; set; }
    /// <summary>% Processor Performance (_Total).</summary>
    public double? Perf { get; set; }
    /// <summary>Effective clock of the whole CPU, MHz.</summary>
    public double? ClockMHz { get; set; }
    /// <summary>Effective clock of each logical core, MHz.</summary>
    public double[]? CoreClocksMHz { get; set; }
    /// <summary>Temperature of each logical core (from its physical core), °C. NaN = unknown.</summary>
    public double[]? CoreTemps { get; set; }
}

public sealed class GpuData
{
    public string Luid { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Integrated { get; set; }
    public double? Load { get; set; }
    public double? Temp { get; set; }
    public double? VramUsedBytes { get; set; }
    public double? VramTotalBytes { get; set; }
    /// <summary>Top processes on this GPU, highest first.</summary>
    public IReadOnlyList<ProcInfo>? Tops { get; set; }
}

public sealed class MemData
{
    public ulong AvailBytes { get; set; }
    public ulong TotalBytes { get; set; }
    public ulong CommitBytes { get; set; }
    public ulong CommitLimitBytes { get; set; }
    public double? PagesInPerSec { get; set; }
}

public sealed record DiskData(string Label, string Volumes, double ActivePercent, double Queue);

public sealed record VolumeData(string Root, double FreeBytes, double TotalBytes);

/// <summary>All sensor values of one sample. Null parts = source failed (item hidden).</summary>
public sealed class Snapshot
{
    public DateTime Time { get; set; }
    public CpuData? Cpu { get; set; }
    public List<AdapterRate>? Net { get; set; }
    public NetQuality? Quality { get; set; }
    public List<GpuData>? Gpus { get; set; }
    public MemData? Mem { get; set; }
    public List<DiskData>? Disks { get; set; }
    public List<VolumeData>? Volumes { get; set; }
    /// <summary>Top processes, highest first (a few, so the shown one can stay).</summary>
    public IReadOnlyList<ProcInfo>? TopCpu { get; set; }
    public IReadOnlyList<ProcInfo>? TopMem { get; set; }
    public IReadOnlyList<ProcInfo>? TopDisk { get; set; }
    public IReadOnlyList<ClaudeLine>? Claude { get; set; }
}

/// <summary>What the sampler must collect for the next samples (process scans are costly).</summary>
public sealed class Needs
{
    public bool TopCpu { get; set; }
    public bool TopMem { get; set; }
    public bool TopDisk { get; set; }
}
