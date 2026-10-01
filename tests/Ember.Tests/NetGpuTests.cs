using System.Buffers.Binary;
using System.Text;
using Ember.Core.Gpu;
using Ember.Core.Net;

namespace Ember.Tests;

public class NetworkTests
{
    private static byte[] Row(ulong luid, string alias, string desc, uint type, uint tunnel, byte flags, ulong inOct, ulong outOct)
    {
        var b = new byte[IfTableLayout.RowSize];
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(IfTableLayout.OffLuid), luid);
        Encoding.Unicode.GetBytes(alias).CopyTo(b, IfTableLayout.OffAlias);
        Encoding.Unicode.GetBytes(desc).CopyTo(b, IfTableLayout.OffDescription);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(IfTableLayout.OffType), type);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(IfTableLayout.OffTunnelType), tunnel);
        b[IfTableLayout.OffFlags] = flags;
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(IfTableLayout.OffOperStatus), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(IfTableLayout.OffInOctets), inOct);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(IfTableLayout.OffOutOctets), outOct);
        return b;
    }

    [Fact]
    public void Layout_OffsetsMatchNetioapi()
    {
        // Values from netioapi.h on x64: alias at 28, InOctets at 1208, OutOctets at 1280, size 1352.
        Assert.Equal(1352, IfTableLayout.RowSize);
        Assert.Equal(28 + 257 * 2, IfTableLayout.OffDescription);
        Assert.Equal(IfTableLayout.OffInOctets + 9 * 8, IfTableLayout.OffOutOctets);
        Assert.Equal(IfTableLayout.RowSize, IfTableLayout.OffOutOctets + 9 * 8);
    }

    [Fact]
    public void ParseTable_ReadsRows()
    {
        var table = new byte[8 + 2 * IfTableLayout.RowSize];
        BinaryPrimitives.WriteUInt32LittleEndian(table, 2);
        Row(1, "Ethernet", "Intel(R) Ethernet", 6, 0, 0x05, 1000, 2000).CopyTo(table, 8);
        Row(2, "Loopback", "Software Loopback Interface 1", 24, 0, 0, 5, 5).CopyTo(table, 8 + IfTableLayout.RowSize);
        var rows = IfTableLayout.ParseTable(table);
        Assert.Equal(2, rows.Count);
        Assert.Equal("Ethernet", rows[0].Alias);
        Assert.Equal("Intel(R) Ethernet", rows[0].Description);
        Assert.True(rows[0].ConnectorPresent);
        Assert.True(rows[0].HardwareInterface);
        Assert.Equal(1000UL, rows[0].InOctets);
        Assert.Equal(2000UL, rows[0].OutOctets);
        Assert.Equal(24U, rows[1].Type);
    }

    private static readonly List<string> Excluded = new Ember.Core.Config.NetworkSettings().ExcludedAdapters;

    [Theory]
    [InlineData("Ethernet", "Intel(R) Ethernet Controller I225-V", 6u, 0u, (byte)0x05, true)]
    [InlineData("Wi-Fi", "Intel(R) Wi-Fi 6E AX211", 71u, 0u, (byte)0x05, true)]
    [InlineData("Loopback", "Software Loopback Interface 1", 24u, 0u, (byte)0x00, false)]
    [InlineData("Ethernet-WFP", "Intel(R) Ethernet-WFP Native MAC Layer LightWeight Filter-0000", 6u, 0u, (byte)0x06, false)]
    [InlineData("vEthernet (WSL)", "Hyper-V Virtual Ethernet Adapter", 6u, 0u, (byte)0x04, false)]
    [InlineData("Tailscale", "Tailscale Tunnel", 53u, 0u, (byte)0x04, false)]
    [InlineData("WG", "WireGuard Tunnel", 53u, 0u, (byte)0x04, false)]
    [InlineData("Teredo", "Teredo Tunneling Pseudo-Interface", 131u, 14u, (byte)0x00, false)]
    [InlineData("Ethernet 2", "Realtek USB GbE", 6u, 0u, (byte)0x01, false)] // no connector
    public void Filter_PhysicalOnly(string alias, string desc, uint type, uint tunnel, byte flags, bool expected)
    {
        var row = IfTableLayout.ParseRow(Row(1, alias, desc, type, tunnel, flags, 0, 0));
        Assert.Equal(expected, AdapterFilter.IsPhysical(row, Excluded));
    }

    [Fact]
    public void Rates_FromCounterDifference()
    {
        var r = new NetRates();
        var t = new DateTime(2026, 1, 1);
        var a = IfTableLayout.ParseRow(Row(1, "Ethernet", "x", 6, 0, 5, 0, 0));
        Assert.Empty(r.Update(new[] { a }, t));
        var b = IfTableLayout.ParseRow(Row(1, "Ethernet", "x", 6, 0, 5, 7_625_000, 262_500));
        var rates = r.Update(new[] { b }, t.AddSeconds(1));
        Assert.Single(rates);
        Assert.Equal(61e6, rates[0].DownBps, 3);
        Assert.Equal(2.1e6, rates[0].UpBps, 3);
        // Counter reset → 0, not negative.
        var c = IfTableLayout.ParseRow(Row(1, "Ethernet", "x", 6, 0, 5, 10, 10));
        Assert.Equal(0, r.Update(new[] { c }, t.AddSeconds(2))[0].DownBps);
    }

    [Fact]
    public void Ping_LossAndMedian()
    {
        var p = new PingStats(20);
        for (int i = 0; i < 19; i++) p.Add(10 + i);
        p.Add(null);
        Assert.Equal(5, p.LossPercent, 6);
        // last 5 successes: 24,25,26,27,28 → 26
        Assert.Equal(26, p.MedianLatency(5));
        for (int i = 0; i < 25; i++) p.Add(null);
        Assert.Equal(100, p.LossPercent);
        Assert.Null(new PingStats().MedianLatency());
    }
}

public class GpuTests
{
    [Fact]
    public void ParseEngineName()
    {
        Assert.True(GpuNames.TryParseEngine("pid_1234_luid_0x00000000_0x0000d1a5_phys_0_eng_3_engtype_3D", out var e));
        Assert.Equal(1234, e.Pid);
        Assert.Equal("0x00000000_0x0000D1A5", e.Luid);
        Assert.Equal(3, e.Engine);
        Assert.Equal("3D", e.EngineType);
        Assert.True(GpuNames.TryParseEngine("pid_4_luid_0x00000000_0x0000D1A5_phys_0_eng_12_engtype_VideoDecode", out var v));
        Assert.Equal("VideoDecode", v.EngineType);
        Assert.False(GpuNames.TryParseEngine("_Total", out _));
    }

    [Fact]
    public void AdapterLuidAndDxgiFormatMatch()
    {
        Assert.Equal("0x00000000_0x0000D1A5", GpuNames.AdapterLuid("luid_0x00000000_0x0000D1A5_phys_0"));
        Assert.Equal("0x00000000_0x0000D1A5", GpuNames.LuidText(0, 0xD1A5));
        Assert.Equal("0x00000001_0x0000D1A5", GpuNames.LuidText(1, 0xD1A5));
        Assert.Null(GpuNames.AdapterLuid("_Total"));
    }

    [Fact]
    public void Aggregate_BusiestEngineAndTopProcess()
    {
        const string L = "luid_0x00000000_0x0000D1A5";
        var samples = new List<(string, double)>
        {
            ($"pid_100_{L}_phys_0_eng_0_engtype_3D", 40),
            ($"pid_200_{L}_phys_0_eng_0_engtype_3D", 18),
            ($"pid_100_{L}_phys_0_eng_2_engtype_Copy", 5),
            ($"pid_300_{L}_phys_0_eng_4_engtype_VideoDecode", 30),
            ("pid_100_luid_0x00000000_0x0000AAAA_phys_0_eng_0_engtype_3D", 3),
        };
        var r = GpuAggregator.Aggregate(samples);
        Assert.Equal(2, r.Count);
        var a = r["0x00000000_0x0000D1A5"];
        Assert.Equal(58, a.Load, 6); // 3D engine: 40 + 18
        Assert.Equal("3D", a.BusiestEngineType);
        Assert.Equal(100, a.TopPid);
        Assert.Equal(40, a.TopPidLoad, 6);
        Assert.Equal(3, r["0x00000000_0x0000AAAA"].Load, 6);
    }

    [Fact]
    public void Aggregate_LimitsTo100()
    {
        var r = GpuAggregator.Aggregate(new[]
        {
            ("pid_1_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 70.0),
            ("pid_2_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 60.0),
        });
        Assert.Equal(100, r.Values.Single().Load);
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4080", false, "4080")]
    [InlineData("NVIDIA GeForce GTX 1660", false, "1660")]
    [InlineData("AMD Radeon RX 7900 XTX", false, "7900")]
    [InlineData("Intel(R) Arc(TM) A770", false, "A770")]
    [InlineData("Intel(R) UHD Graphics 770", true, "770")]
    [InlineData("AMD Radeon(TM) Graphics", true, "igpu")]
    [InlineData("AMD Radeon 780M Graphics", true, "780M")]
    [InlineData("NVIDIA GeForce RTX 4060 Ti", false, "4060")]
    [InlineData("Some GPU", false, "gpu")]
    public void ShortLabel_ShowsModel(string name, bool integrated, string expected)
        => Assert.Equal(expected, GpuNames.ShortLabel(name, integrated));

    [Fact]
    public void Labels_FirstKeepsModel_OthersNumbered()
    {
        var l = GpuNames.Labels(new[] { ("NVIDIA GeForce RTX 4090", false), ("NVIDIA GeForce RTX 4090", false), ("Intel UHD 770", true) });
        Assert.Equal(new[] { "4090", "40#2", "770" }, l);
        Assert.Equal(new[] { "4090" }, GpuNames.Labels(new[] { ("NVIDIA GeForce RTX 4090", false) }));
    }

    [Fact]
    public void Dedupe_SameHardwareListedTwice_KeepsTheActiveOne()
    {
        var a = new[] { ("4090", "luid1", false), ("4090", "luid2", true), ("770", "luid3", false) };
        var kept = GpuDedupe.Select(a, x => x.Item1, x => x.Item3);
        Assert.Equal(new[] { "luid2", "luid3" }, kept.Select(x => x.Item2));
        // Neither active: the first one.
        var idle = new[] { ("4090", "luid1", false), ("4090", "luid2", false) };
        Assert.Equal(new[] { "luid1" }, GpuDedupe.Select(idle, x => x.Item1, x => x.Item3).Select(x => x.Item2));
        // Both active (real twin GPUs): both.
        var twins = new[] { ("4090", "luid1", true), ("4090", "luid2", true) };
        Assert.Equal(2, GpuDedupe.Select(twins, x => x.Item1, x => x.Item3).Count);
    }

    [Fact]
    public void Integrated_ByDedicatedMemory()
    {
        Assert.True(GpuAggregator.IsIntegrated(128UL * 1024 * 1024));
        Assert.False(GpuAggregator.IsIntegrated(8UL * 1024 * 1024 * 1024));
    }
}
