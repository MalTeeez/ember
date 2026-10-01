using Ember.Core.Claude;
using Ember.Core.Net;

namespace Ember.Core.Status;

/// <summary>A process for a "└ top process" line.</summary>
public sealed record ProcInfo(int Pid, string Name, string ValueText);

public sealed class CpuData
{
    /// <summary>Combined load, % (may be above 100 with turbo; display is limited to 100).</summary>
    public double Load { get; init; }
    /// <summary>Raw load for each logical core, %.</summary>
    public double[]? CoreLoads { get; init; }
    /// <summary>Core group (efficiency class) for each logical core. Null = one group.</summary>
    public int[]? CoreGroups { get; init; }
    /// <summary>Package temperature, °C.</summary>
    public double? Temp { get; init; }
    /// <summary>% Processor Performance (_Total).</summary>
    public double? Perf { get; init; }
}

public sealed class GpuData
{
    public required string Luid { get; init; }
    public required string Name { get; init; }
    public bool Integrated { get; init; }
    public double? Load { get; init; }
    public double? Temp { get; init; }
    public double? VramUsedBytes { get; init; }
    public double? VramTotalBytes { get; init; }
    public ProcInfo? Top { get; init; }
    public double TopLoad { get; init; }
}

public sealed class MemData
{
    public ulong AvailBytes { get; init; }
    public ulong TotalBytes { get; init; }
    public ulong CommitBytes { get; init; }
    public ulong CommitLimitBytes { get; init; }
    public double? PagesInPerSec { get; init; }
}

public sealed record DiskData(string Label, string Volumes, double ActivePercent, double Queue);

public sealed record VolumeData(string Root, double FreeBytes, double TotalBytes);

/// <summary>All sensor values of one sample. Null parts = source failed (item hidden).</summary>
public sealed class Snapshot
{
    public DateTime Time { get; init; }
    public CpuData? Cpu { get; init; }
    public List<AdapterRate>? Net { get; init; }
    public NetQuality? Quality { get; set; }
    public List<GpuData>? Gpus { get; init; }
    public MemData? Mem { get; init; }
    public List<DiskData>? Disks { get; init; }
    public List<VolumeData>? Volumes { get; init; }
    public ProcInfo? TopCpu { get; init; }
    public ProcInfo? TopMem { get; init; }
    public ProcInfo? TopDisk { get; init; }
    public IReadOnlyList<ClaudeLine>? Claude { get; set; }
}

/// <summary>What the sampler must collect for the next samples (process scans are costly).</summary>
public sealed class Needs
{
    public bool TopCpu { get; set; }
    public bool TopMem { get; set; }
    public bool TopDisk { get; set; }
}
