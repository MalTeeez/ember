using Ember.Core.Claude;
using Ember.Core.Config;
using Ember.Core.Net;
using Ember.Core.Render;
using Ember.Core.Status;

namespace Ember.Tests;

public class ComposerTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 12, 0, 0);

    private static Snapshot Snap(int sec, double load = 23, double? temp = 50, List<AdapterRate>? net = null,
        MemData? mem = null, List<VolumeData>? vols = null, IReadOnlyList<ClaudeLine>? claude = null,
        double[]? cores = null, ProcInfo? topCpu = null, List<GpuData>? gpus = null, double? perf = 100)
        => new()
        {
            Time = T0.AddSeconds(sec),
            Cpu = new CpuData { Load = load, Temp = temp, Perf = perf, CoreLoads = cores },
            Net = net ?? new List<AdapterRate> { new("Ethernet", 61e6, 2.1e6) },
            Mem = mem,
            Volumes = vols,
            Claude = claude,
            TopCpu = topCpu,
            Gpus = gpus,
        };

    [Fact]
    public void NormalState_CpuAndNetwork()
    {
        var c = new StatusComposer();
        var lines = c.Compose(Snap(0), new EmberSettings());
        Assert.Equal(3, lines.Count);
        Assert.StartsWith("cpu  23%", lines[0].Text);
        Assert.StartsWith("\u2193   61.0 Mbps", lines[1].Text);
        Assert.EndsWith(" pk 61", lines[1].Text);
        Assert.StartsWith("\u2191    2.1 Mbps", lines[2].Text);
        Assert.All(lines, l => Assert.Equal(Layout.Width, l.Length));
        // graph columns are braille
        Assert.All(lines[0].Text.Substring(Layout.GraphCol, Layout.GraphChars), ch => Assert.InRange(ch, '\u2800', '\u28FF'));
    }

    [Fact]
    public void LoadAbove100_IsLimited()
    {
        var lines = new StatusComposer().Compose(Snap(0, load: 130), new EmberSettings());
        Assert.StartsWith("cpu 100%", lines[0].Text);
    }

    [Fact]
    public void Temperature_ShowsAfterDelay_WithGradient()
    {
        var c = new StatusComposer();
        var cfg = new EmberSettings();
        for (int i = 0; i < 5; i++)
            Assert.DoesNotContain("\u00B0", c.Compose(Snap(i, temp: 92), cfg)[0].Text);
        var line = c.Compose(Snap(5, temp: 92), cfg)[0];
        Assert.Contains(" 92\u00B0", line.Text);
        Assert.Contains(line.Runs, r => r.Text.Contains('\u00B0') && r.Color == Palette.Red);
    }

    [Fact]
    public void FailedSource_HidesImmediately()
    {
        var c = new StatusComposer();
        var cfg = new EmberSettings();
        for (int i = 0; i <= 6; i++) c.Compose(Snap(i, temp: 92), cfg);
        Assert.Contains("\u00B0", c.Compose(Snap(7, temp: 92), cfg)[0].Text);
        Assert.DoesNotContain("\u00B0", c.Compose(Snap(8, temp: null), cfg)[0].Text);
        // Coming back needs the full show delay again.
        Assert.DoesNotContain("\u00B0", c.Compose(Snap(9, temp: 92), cfg)[0].Text);
    }

    [Fact]
    public void TopProcess_RequestedWhenLoadHigh_ThenShown()
    {
        var c = new StatusComposer();
        var cfg = new EmberSettings();
        c.Compose(Snap(0, load: 80), cfg);
        Assert.True(c.Needs.TopCpu);
        List<Line> lines = new();
        for (int i = 1; i <= 5; i++) lines = c.Compose(Snap(i, load: 80, topCpu: new ProcInfo(42, "game.exe", "58%")), cfg);
        var top = lines.Single(l => l.Click == ClickKind.Process);
        Assert.Equal(42, top.Pid);
        Assert.StartsWith("  \u2514 game.exe", top.Text);
        Assert.EndsWith("58%", top.Text);
    }

    [Fact]
    public void TopProcess_NotRequestedWhenIdle()
    {
        var c = new StatusComposer();
        c.Compose(Snap(0, load: 10), new EmberSettings());
        Assert.False(c.Needs.TopCpu);
        Assert.False(c.Needs.TopMem);
        Assert.False(c.Needs.TopDisk);
    }

    [Fact]
    public void OutlierCore_LineWithBarAndMedianMarker()
    {
        var c = new StatusComposer();
        var cfg = new EmberSettings();
        var cores = new double[] { 10, 10, 10, 10, 10, 10, 10, 100 };
        List<Line> lines = new();
        for (int i = 0; i <= 6; i++) lines = c.Compose(Snap(i, load: 21, cores: cores), cfg);
        var core = lines.Single(l => l.Text.StartsWith("  c7"));
        Assert.Contains("\u2502", core.Text);
        Assert.EndsWith("100%", core.Text);
    }

    [Fact]
    public void Throttling()
    {
        var c = new StatusComposer();
        var cfg = new EmberSettings();
        List<Line> lines = new();
        for (int i = 0; i <= 5; i++) lines = c.Compose(Snap(i, load: 70, perf: 60), cfg);
        Assert.Contains(lines, l => l.Text.StartsWith("  throttled") && l.Text.EndsWith("60%"));
    }

    [Fact]
    public void MultipleActiveAdapters_ShowNames()
    {
        var c = new StatusComposer();
        var net = new List<AdapterRate> { new("Ethernet", 50e6, 1e6), new("Wi-Fi", 11e6, 1.1e6) };
        List<Line> lines = new();
        for (int i = 0; i <= 5; i++) lines = c.Compose(Snap(i, net: net), new EmberSettings());
        Assert.Contains(lines, l => l.Text.Contains("Ethernet"));
        Assert.Contains(lines, l => l.Text.Contains("Wi-Fi"));

        var single = new StatusComposer();
        var one = new List<AdapterRate> { new("Ethernet", 50e6, 1e6), new("Wi-Fi", 0, 0) };
        for (int i = 0; i <= 5; i++) lines = single.Compose(Snap(i, net: one), new EmberSettings());
        Assert.DoesNotContain(lines, l => l.Text.Contains("Ethernet"));
    }

    [Fact]
    public void MemorySection_HeaderOnlyWhenVisible()
    {
        var c = new StatusComposer();
        var cfg = new EmberSettings();
        const ulong G = 1024UL * 1024 * 1024;
        var low = new MemData { AvailBytes = G / 2, TotalBytes = 16 * G, CommitBytes = 10 * G, CommitLimitBytes = 32 * G, PagesInPerSec = 10 };
        var ok = new MemData { AvailBytes = 8 * G, TotalBytes = 16 * G, CommitBytes = 10 * G, CommitLimitBytes = 32 * G, PagesInPerSec = 10 };
        Assert.DoesNotContain(c.Compose(Snap(0, mem: ok), cfg), l => l.Text.Contains("M E M O R Y"));
        List<Line> lines = new();
        for (int i = 1; i <= 6; i++) lines = c.Compose(Snap(i, mem: low), cfg);
        Assert.True(c.Needs.TopMem);
        int h = lines.FindIndex(l => l.Text.Contains("M E M O R Y"));
        Assert.True(h > 0);
        Assert.Equal("", lines[h - 1].Text.Trim());
        Assert.StartsWith("ram", lines[h + 1].Text);
        Assert.EndsWith("3%", lines[h + 1].Text);
        Assert.Contains(lines[h + 1].Runs, r => r.Color == Palette.Red);
    }

    [Fact]
    public void FreeSpace_LowGbIsAtLeastAmber()
    {
        var c = new StatusComposer();
        const double G = 1024.0 * 1024 * 1024;
        var vols = new List<VolumeData> { new("D:\\", 15 * G, 100 * G) }; // 15% but < 20 GB
        List<Line> lines = new();
        for (int i = 0; i <= 5; i++) lines = c.Compose(Snap(i, vols: vols), new EmberSettings());
        var line = lines.Single(l => l.Text.StartsWith("D:"));
        Assert.Contains(line.Runs, r => r.Color == Palette.Amber);
    }

    [Fact]
    public void Claude_TwoLinesEach_AlwaysVisible()
    {
        var claude = new List<ClaudeLine>
        {
            new("5h", 42, TimeSpan.FromMinutes(190), new ProjectionResult(ProjectionKind.Lasts, TimeSpan.MaxValue, TimeSpan.FromMinutes(190))),
            new("wk", 18, TimeSpan.FromHours(98), new ProjectionResult(ProjectionKind.Limit, TimeSpan.FromHours(10), TimeSpan.FromHours(98))),
        };
        var lines = new StatusComposer().Compose(Snap(0, claude: claude), new EmberSettings());
        int h = lines.FindIndex(l => l.Text.StartsWith("\u2500 C L A U D E"));
        Assert.True(h > 0);
        Assert.StartsWith("5h  ████████▍", lines[h + 1].Text);
        Assert.EndsWith("42%", lines[h + 1].Text);
        Assert.StartsWith("    \u21BB 3h10", lines[h + 2].Text);
        Assert.EndsWith("lasts", lines[h + 2].Text);
        Assert.Contains(lines[h + 2].Runs, r => r.Text.EndsWith("lasts") && r.Color == Palette.Dim);
        Assert.EndsWith("limit 10h00", lines[h + 4].Text);
        Assert.Contains(lines[h + 4].Runs, r => r.Text.StartsWith("limit") && r.Color == Palette.Red);
    }

    [Fact]
    public void Claude_HiddenWhenNull()
    {
        var lines = new StatusComposer().Compose(Snap(0, claude: null), new EmberSettings());
        Assert.DoesNotContain(lines, l => l.Text.Contains("C L A U D E"));
    }

    [Fact]
    public void Gpu_LoadAndTemp_OneLine()
    {
        var c = new StatusComposer();
        var gpus = new List<GpuData>
        {
            new() { Luid = "0x0_0x1", Name = "NVIDIA GeForce RTX 4080", Load = 64, Temp = 71,
                    Top = new ProcInfo(7, "game.exe", "58%"), TopLoad = 58 },
        };
        List<Line> lines = new();
        for (int i = 0; i <= 5; i++) lines = c.Compose(Snap(i, gpus: gpus), new EmberSettings());
        int h = lines.FindIndex(l => l.Text.StartsWith("\u2500 G P U"));
        Assert.StartsWith("rtx  64%  71\u00B0 ", lines[h + 1].Text);
        Assert.Equal(7, lines[h + 2].Pid);
    }

    [Fact]
    public void Gpu_VramHiddenForIntegratedAndWhenOff()
    {
        var c = new StatusComposer();
        var cfg = new EmberSettings();
        cfg.Items.GpuVram = true;
        const double G = 1024.0 * 1024 * 1024;
        var gpus = new List<GpuData>
        {
            new() { Luid = "a", Name = "Intel UHD", Integrated = true, Load = 0, VramUsedBytes = 0.99 * G, VramTotalBytes = G },
            new() { Luid = "b", Name = "RTX", Load = 0, VramUsedBytes = 7.8 * G, VramTotalBytes = 8 * G },
        };
        List<Line> lines = new();
        for (int i = 0; i <= 5; i++) lines = c.Compose(Snap(i, gpus: gpus), cfg);
        Assert.Single(lines, l => l.Text.StartsWith("  vram"));
        Assert.Contains(lines, l => l.Text.StartsWith("  vram  7.8/8.0 GB"));
        cfg.Items.GpuVram = false;
        lines = c.Compose(Snap(6, gpus: gpus), cfg);
        Assert.DoesNotContain(lines, l => l.Text.StartsWith("  vram"));
    }

    [Fact]
    public void ItemsOff_AreHidden()
    {
        var cfg = new EmberSettings();
        cfg.Items.CpuLoad = false;
        cfg.Items.Network = false;
        Assert.Empty(new StatusComposer().Compose(Snap(0), cfg));
    }
}
