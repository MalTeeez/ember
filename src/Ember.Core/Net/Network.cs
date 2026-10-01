using System.Buffers.Binary;
using System.Text;

namespace Ember.Core.Net;

/// <summary>The fields of MIB_IF_ROW2 that Ember uses.</summary>
public sealed record IfRow(
    ulong Luid, uint Index, string Alias, string Description, uint Type, uint TunnelType, uint MediaType,
    byte Flags, uint OperStatus, ulong InOctets, ulong OutOctets, ulong InPackets = 0, ulong OutPackets = 0)
{
    public bool HardwareInterface => (Flags & 0x01) != 0;
    public bool FilterInterface => (Flags & 0x02) != 0;
    public bool ConnectorPresent => (Flags & 0x04) != 0;
}

/// <summary>
/// Byte layout of MIB_IF_TABLE2 / MIB_IF_ROW2 (x64, netioapi.h). Size of a row is 1352 bytes.
/// The table has ULONG NumEntries, then the rows at offset 8.
/// </summary>
public static class IfTableLayout
{
    public const int RowSize = 1352;
    public const int TableHeader = 8;
    public const int OffLuid = 0, OffIndex = 8, OffAlias = 28, OffDescription = 542, NameChars = 257;
    public const int OffType = 1128, OffTunnelType = 1132, OffMediaType = 1136;
    public const int OffFlags = 1152, OffOperStatus = 1156;
    public const int OffInOctets = 1208, OffOutOctets = 1280;
    // Unicast and non-unicast packet counters follow the octet counters.
    public const int OffInUcastPkts = 1216, OffInNUcastPkts = 1224, OffOutUcastPkts = 1288, OffOutNUcastPkts = 1296;

    public static IfRow ParseRow(ReadOnlySpan<byte> row)
    {
        if (row.Length < RowSize) throw new ArgumentException("row too short");
        return new IfRow(
            BinaryPrimitives.ReadUInt64LittleEndian(row[OffLuid..]),
            BinaryPrimitives.ReadUInt32LittleEndian(row[OffIndex..]),
            ReadWString(row.Slice(OffAlias, NameChars * 2)),
            ReadWString(row.Slice(OffDescription, NameChars * 2)),
            BinaryPrimitives.ReadUInt32LittleEndian(row[OffType..]),
            BinaryPrimitives.ReadUInt32LittleEndian(row[OffTunnelType..]),
            BinaryPrimitives.ReadUInt32LittleEndian(row[OffMediaType..]),
            row[OffFlags],
            BinaryPrimitives.ReadUInt32LittleEndian(row[OffOperStatus..]),
            BinaryPrimitives.ReadUInt64LittleEndian(row[OffInOctets..]),
            BinaryPrimitives.ReadUInt64LittleEndian(row[OffOutOctets..]),
            BinaryPrimitives.ReadUInt64LittleEndian(row[OffInUcastPkts..]) + BinaryPrimitives.ReadUInt64LittleEndian(row[OffInNUcastPkts..]),
            BinaryPrimitives.ReadUInt64LittleEndian(row[OffOutUcastPkts..]) + BinaryPrimitives.ReadUInt64LittleEndian(row[OffOutNUcastPkts..]));
    }

    public static List<IfRow> ParseTable(ReadOnlySpan<byte> table)
    {
        uint n = BinaryPrimitives.ReadUInt32LittleEndian(table);
        var rows = new List<IfRow>((int)Math.Min(n, 1024));
        for (int i = 0; i < n; i++)
        {
            int off = TableHeader + i * RowSize;
            if (off + RowSize > table.Length) break;
            rows.Add(ParseRow(table.Slice(off, RowSize)));
        }
        return rows;
    }

    private static string ReadWString(ReadOnlySpan<byte> bytes)
    {
        var s = Encoding.Unicode.GetString(bytes);
        int z = s.IndexOf('\0');
        return z >= 0 ? s[..z] : s;
    }
}

/// <summary>Physical adapter filter.</summary>
public static class AdapterFilter
{
    public const uint IfTypeSoftwareLoopback = 24;
    public const uint IfTypeTunnel = 131;
    public const uint IfTypePpp = 23;
    public const uint TunnelTypeNone = 0;

    /// <summary>
    /// True for physical adapters: ConnectorPresent set, not a filter row, not loopback/tunnel/PPP,
    /// and the description or alias contains none of the excluded words (case-insensitive).
    /// </summary>
    public static bool IsPhysical(IfRow row, IEnumerable<string> excluded)
    {
        if (!row.ConnectorPresent) return false;
        if (row.FilterInterface) return false;
        if (row.Type is IfTypeSoftwareLoopback or IfTypeTunnel or IfTypePpp) return false;
        if (row.TunnelType != TunnelTypeNone) return false;
        foreach (var word in excluded)
        {
            if (string.IsNullOrWhiteSpace(word)) continue;
            if (row.Description.Contains(word, StringComparison.OrdinalIgnoreCase) ||
                row.Alias.Contains(word, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }
}

/// <summary>Rate for one adapter, in bits per second, and packets per second.</summary>
public sealed record AdapterRate(string Name, double DownBps, double UpBps, double DownPps = 0, double UpPps = 0);

/// <summary>Converts byte counters into rates (difference each sample).</summary>
public sealed class NetRates
{
    private readonly Dictionary<ulong, (ulong In, ulong Out, ulong InPk, ulong OutPk)> _last = new();
    private DateTime _lastTime;

    public List<AdapterRate> Update(IEnumerable<IfRow> physical, DateTime now)
    {
        var result = new List<AdapterRate>();
        double dt = (now - _lastTime).TotalSeconds;
        bool valid = _last.Count > 0 && dt > 0.05;
        var seen = new Dictionary<ulong, (ulong, ulong, ulong, ulong)>();
        foreach (var row in physical)
        {
            if (seen.ContainsKey(row.Luid)) continue;
            seen[row.Luid] = (row.InOctets, row.OutOctets, row.InPackets, row.OutPackets);
            if (valid && _last.TryGetValue(row.Luid, out var prev))
            {
                // Counter reset or wrap: treat as 0.
                static double Rate(ulong now, ulong before, double dt, double factor) => now >= before ? (now - before) * factor / dt : 0;
                result.Add(new AdapterRate(row.Alias,
                    Rate(row.InOctets, prev.In, dt, 8), Rate(row.OutOctets, prev.Out, dt, 8),
                    Rate(row.InPackets, prev.InPk, dt, 1), Rate(row.OutPackets, prev.OutPk, dt, 1)));
            }
            else if (valid)
            {
                result.Add(new AdapterRate(row.Alias, 0, 0));
            }
        }
        _last.Clear();
        foreach (var kv in seen) _last[kv.Key] = kv.Value;
        _lastTime = now;
        return valid ? result : new List<AdapterRate>();
    }

    public bool HasBaseline => _last.Count > 0;

    public void Reset() => _last.Clear();
}

/// <summary>Results of the last pings to one target. Null entry = lost.</summary>
public sealed class PingStats
{
    private readonly Queue<double?> _results = new();
    public int Capacity { get; }
    public PingStats(int capacity = 20) => Capacity = capacity;

    public void Add(double? rttMs)
    {
        _results.Enqueue(rttMs);
        while (_results.Count > Capacity) _results.Dequeue();
    }

    public int Count => _results.Count;

    /// <summary>Loss in % of the last pings (0 when empty).</summary>
    public double LossPercent => _results.Count == 0 ? 0 : 100.0 * _results.Count(r => r == null) / _results.Count;

    /// <summary>Median round-trip time of the last <paramref name="n"/> successful replies (null if none).</summary>
    public double? MedianLatency(int n = 5)
    {
        var ok = _results.Where(r => r != null).Select(r => r!.Value).ToList();
        if (ok.Count == 0) return null;
        var last = ok.Skip(Math.Max(0, ok.Count - n)).ToList();
        return Stats.OutlierStats.Median(last);
    }

    public void Clear() => _results.Clear();
}

/// <summary>Network quality summary.</summary>
public sealed record NetQuality(double GatewayLoss, double InternetLoss, double? GatewayMs, double? InternetMs, bool HasGateway, string GatewayHost = "", string InternetHost = "")
{
    /// <summary>Last traceroute to the internet host (null when off or not done yet).</summary>
    public IReadOnlyList<TraceHop>? Hops { get; init; }
}

/// <summary>One traceroute hop: TTL, the answering address (null = no answer) and the round trip.</summary>
public sealed record TraceHop(int Ttl, string? Address, double? Ms)
{
    /// <summary>Reverse DNS name of the address, if known.</summary>
    public string? HostName { get; init; }
}

public static class Traceroute
{
    /// <summary>
    /// Hops for display: hops without an answer are left out, and the next answering hop is marked
    /// (Skipped = how many were left out before it). Ends at the first hop that is the target.
    /// </summary>
    public static List<(TraceHop Hop, int Skipped)> Collapse(IReadOnlyList<TraceHop> hops, string target)
    {
        var list = new List<(TraceHop, int)>();
        int skipped = 0;
        foreach (var h in hops.OrderBy(h => h.Ttl))
        {
            if (h.Address == null) { skipped++; continue; }
            list.Add((h, skipped));
            skipped = 0;
            if (h.Address == target) break;
        }
        return list;
    }
}
