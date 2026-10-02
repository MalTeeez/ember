using Ember.Core.Claude;
using Ember.Core.Config;
using Ember.Core.Net;
using Ember.Core.Render;
using Ember.Core.Status;

namespace Ember.Tests;

public class ComposerTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 12, 0, 0);

    /// <summary>Default settings without the toolbar, hold 0 (no kept space) unless asked.</summary>
    private static EmberSettings Cfg(double hold = 0)
    {
        var s = new EmberSettings();
        s.Entries.First(e => e.Id == "toolbar").Enabled = false;
        s.Layout.HoldSeconds = hold;
        s.Layout.ProcessStickSeconds = 10;
        return s;
    }

    private static List<ProcInfo> Procs(params (int Pid, string Name, double V)[] p)
        => p.OrderByDescending(x => x.V).Select(x => new ProcInfo(x.Pid, x.Name, x.V, Format.Pct4(x.V))).ToList();

    private static Snapshot Snap(int sec, double load = 23, double? temp = 50, List<AdapterRate>? net = null,
        MemData? mem = null, List<VolumeData>? vols = null, IReadOnlyList<ClaudeLine>? claude = null,
        double[]? cores = null, IReadOnlyList<ProcInfo>? topCpu = null, List<GpuData>? gpus = null, double? perf = 100,
        double? clock = null, double[]? coreTemps = null, double[]? coreClocks = null)
        => new()
        {
            Time = T0.AddSeconds(sec),
            Cpu = new CpuData { Load = load, Temp = temp, Perf = perf, CoreLoads = cores, ClockMHz = clock, CoreTemps = coreTemps, CoreClocksMHz = coreClocks },
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
        var lines = new StatusComposer().Compose(Snap(0, clock: 4200), Cfg());
        // cpu, spacer, net header, down, up
        Assert.Equal(5, lines.Count);
        // label, temperature slot (5..9), graph (10..19), load, clock with its unit
        Assert.StartsWith("cpu  ", lines[0].Text);
        Assert.EndsWith("  23% 4.2GHz", lines[0].Text);
        Assert.All(lines[0].Text.Substring(10, 10), ch => Assert.InRange(ch, '\u2800', '\u28FF'));
        Assert.StartsWith("\u2500\u2500 net \u2500", lines[2].Text);
        Assert.StartsWith("\u2193  61M ", lines[3].Text);
        Assert.EndsWith(" pkt/m", lines[3].Text); // packets of the last minute after the graph
        Assert.StartsWith("\u2191 2.1M ", lines[4].Text);
        Assert.All(lines, l => Assert.Equal(Layout.Width, l.Length));
        Assert.All(lines[3].Text.Substring(7, 14), ch => Assert.InRange(ch, '\u2800', '\u28FF'));
    }

    [Fact]
    public void LabelsAlwaysHaveSpaceBeforeValue()
    {
        var c = new StatusComposer();
        var cfg = Cfg();
        cfg.PreviewAll = true;
        foreach (var l in c.Compose(Snap(0), cfg))
        {
            var t = l.Text;
            if (t.StartsWith("DeathAdder") || t.StartsWith("WH-")) continue; // device battery lines: name, bar, %
            // A label is followed by at least one space before its value (labels are at most 4 characters).
            if (char.IsLetterOrDigit(t[0])) Assert.True(t.IndexOf(' ') is > 0 and <= 9, t);
            // Arrow lines: the arrow, then the 4-character value (which starts with a space or a short number).
            if (t[0] is '\u2193' or '\u2191') Assert.Equal(' ', t[1]);
        }
    }

    [Fact]
    public void LoadAbove100_IsLimited()
    {
        // No load number on the cpu line; the graph's last cell is full (not more).
        var line = new StatusComposer().Compose(Snap(0, load: 130), Cfg())[0].Text;
        Assert.Contains(" 100% ", line);
    }

    [Fact]
    public void Temperature_ShowsAfterDelay_WithThemeGradient()
    {
        var c = new StatusComposer();
        var cfg = Cfg();
        cfg.Fields.CpuTempAlways = false;
        for (int i = 0; i < 5; i++)
            Assert.DoesNotContain("\u00B0", c.Compose(Snap(i, temp: 92), cfg)[0].Text);
        var line = c.Compose(Snap(5, temp: 92), cfg)[0];
        Assert.Contains(" 92\u00B0", line.Text);
        var crit = Rgb.Parse(cfg.Theme.Crit, Palette.Red);
        Assert.Contains(line.Runs, r => r.Text.Contains('\u00B0') && r.Color == crit);
    }

    [Fact]
    public void FailedSource_HidesImmediately()
    {
        var c = new StatusComposer();
        var cfg = Cfg();
        cfg.Fields.CpuTempAlways = false;
        for (int i = 0; i <= 6; i++) c.Compose(Snap(i, temp: 92), cfg);
        Assert.Contains("\u00B0", c.Compose(Snap(7, temp: 92), cfg)[0].Text);
        Assert.DoesNotContain("\u00B0", c.Compose(Snap(8, temp: null), cfg)[0].Text);
        Assert.DoesNotContain("\u00B0", c.Compose(Snap(9, temp: 92), cfg)[0].Text);
    }

    [Fact]
    public void TopProcess_LineWithButtons()
    {
        var c = new StatusComposer();
        var cfg = Cfg();
        c.Compose(Snap(0, load: 80), cfg);
        Assert.True(c.Needs.TopCpu);
        List<Line> lines = new();
        for (int i = 1; i <= 5; i++) lines = c.Compose(Snap(i, load: 80, topCpu: Procs((42, "game.exe", 58))), cfg);
        var top = lines.Single(l => l.Pid == 42);
        Assert.StartsWith("  \u2514 game ", top.Text);
        Assert.Contains(" 58% ", top.Text);
        Assert.Contains(top.Hits, h => h.Action == HitAction.ProcessKill && h.Arg == "42" && h.Width == 2);
        Assert.Contains(top.Hits, h => h.Action == HitAction.ProcessProperties);
        Assert.Contains(top.Hits, h => h.Action == HitAction.ProcessDetails && h.Width == Layout.Width);
        Assert.Contains(top.Runs, r => (r.Style & RunStyle.Icon) != 0);
        Assert.Equal(Layout.Width, top.Length);
    }

    [Fact]
    public void TopProcess_IsSticky()
    {
        var c = new StatusComposer();
        var cfg = Cfg();
        List<Line> lines = new();
        for (int i = 0; i <= 5; i++) lines = c.Compose(Snap(i, load: 80, topCpu: Procs((1, "a.exe", 60), (2, "b.exe", 50))), cfg);
        Assert.Contains(lines, l => l.Pid == 1);
        // b is higher now, but only for a short time: a stays.
        for (int i = 6; i <= 10; i++) lines = c.Compose(Snap(i, load: 80, topCpu: Procs((1, "a.exe", 40), (2, "b.exe", 70))), cfg);
        Assert.Contains(lines, l => l.Pid == 1 && l.Text.Contains(" 40%"));
        for (int i = 11; i <= 17; i++) lines = c.Compose(Snap(i, load: 80, topCpu: Procs((1, "a.exe", 40), (2, "b.exe", 70))), cfg);
        Assert.Contains(lines, l => l.Pid == 2);
    }

    [Fact]
    public void TopProcess_NotRequestedWhenIdle()
    {
        var c = new StatusComposer();
        c.Compose(Snap(0, load: 10), Cfg());
        Assert.False(c.Needs.TopCpu);
        Assert.False(c.Needs.TopMem);
        Assert.False(c.Needs.TopDisk);
    }

    [Fact]
    public void HotCore_LoadClockTempGraph()
    {
        var c = new StatusComposer();
        var cfg = Cfg();
        var cores = new double[] { 10, 10, 10, 10, 10, 10, 10, 100 };
        var temps = Enumerable.Repeat(50.0, 8).ToArray();
        temps[7] = 88;
        var clocks = Enumerable.Repeat(3000.0, 8).ToArray();
        clocks[7] = 5100;
        List<Line> lines = new();
        for (int i = 0; i <= 6; i++) lines = c.Compose(Snap(i, load: 21, cores: cores, coreTemps: temps, coreClocks: clocks), cfg);
        // Indented by 2 (part of the cpu line): label, temperature, graph, load, clock.
        var core = lines.Single(l => l.Text.StartsWith("  c7"));
        Assert.StartsWith("  c7   88\u00B0 ", core.Text);
        Assert.EndsWith(" 100% 5.1GHz", core.Text);
        Assert.All(core.Text.Substring(11, 9), ch => Assert.InRange(ch, '\u2800', '\u28FF'));
    }

    [Fact]
    public void Throttling()
    {
        var c = new StatusComposer();
        List<Line> lines = new();
        for (int i = 0; i <= 5; i++) lines = c.Compose(Snap(i, load: 70, perf: 60), Cfg());
        Assert.Contains(lines, l => l.Text.Contains("throttled") && l.Text.EndsWith(" 60%"));
    }

    [Fact]
    public void MultipleActiveAdapters_ShowNames()
    {
        var c = new StatusComposer();
        var net = new List<AdapterRate> { new("Ethernet", 50e6, 1e6), new("Wi-Fi", 11e6, 1.1e6) };
        List<Line> lines = new();
        for (int i = 0; i <= 5; i++) lines = c.Compose(Snap(i, net: net), Cfg());
        Assert.Contains(lines, l => l.Text.Contains("Ethernet"));
        Assert.Contains(lines, l => l.Text.Contains("Wi-Fi"));
    }

    [Fact]
    public void Memory_HeaderOnlyWhenVisible_LowerCaseRule()
    {
        var c = new StatusComposer();
        var cfg = Cfg();
        const ulong G = 1024UL * 1024 * 1024;
        var low = new MemData { AvailBytes = G / 2, TotalBytes = 16 * G, CommitBytes = 10 * G, CommitLimitBytes = 32 * G, PagesInPerSec = 10 };
        var ok = new MemData { AvailBytes = 8 * G, TotalBytes = 16 * G, CommitBytes = 10 * G, CommitLimitBytes = 32 * G, PagesInPerSec = 10 };
        Assert.DoesNotContain(c.Compose(Snap(0, mem: ok), cfg), l => l.Text.Contains("memory"));
        List<Line> lines = new();
        for (int i = 1; i <= 6; i++) lines = c.Compose(Snap(i, mem: low), cfg);
        int h = lines.FindIndex(l => l.Text.StartsWith("\u2500\u2500 memory \u2500"));
        Assert.True(h > 0);
        Assert.Equal("", lines[h - 1].Text.Trim());
        // No "ram" label inside the memory section: used / total, bar, percent used.
        Assert.StartsWith("used       16GB / 16GB ", lines[h + 1].Text);
        Assert.EndsWith(" 97%", lines[h + 1].Text);
    }

    [Fact]
    public void HeightIsKept_ThenShrinksAfterHold()
    {
        var c = new StatusComposer();
        var cfg = Cfg(hold: 30);
        cfg.Layout.KeptSpace = "bottom";
        cfg.Intervals.HideDelaySeconds = 0;
        const ulong G = 1024UL * 1024 * 1024;
        var low = new MemData { AvailBytes = G / 2, TotalBytes = 16 * G, CommitBytes = 1, CommitLimitBytes = 32 * G };
        var ok = new MemData { AvailBytes = 8 * G, TotalBytes = 16 * G, CommitBytes = 1, CommitLimitBytes = 32 * G };
        int shown = 0;
        for (int i = 0; i <= 6; i++) shown = c.Compose(Snap(i, mem: low), cfg).Count;
        // The item hides, but its space stays for the hold time: as empty lines at the bottom
        // (the section header too, as it has nothing to show).
        var kept = c.Compose(Snap(8, mem: ok), cfg);
        Assert.Equal(shown, kept.Count);
        Assert.True(kept[^1].IsPadding);
        Assert.DoesNotContain(kept, l => l.Text.Contains("memory"));
        Assert.False(kept[0].IsPadding);
        var later = c.Compose(Snap(40, mem: ok), cfg);
        Assert.True(later.Count < shown);
        Assert.DoesNotContain(later, l => l.Text.Contains("memory"));
    }

    [Fact]
    public void HeightIsKept_Section_EmptyLinesAtSectionEnd()
    {
        var c = new StatusComposer();
        var cfg = Cfg(hold: 30);
        cfg.Intervals.HideDelaySeconds = 0;
        const ulong G = 1024UL * 1024 * 1024;
        var low = new MemData { AvailBytes = G / 2, TotalBytes = 16 * G, CommitBytes = 31 * G, CommitLimitBytes = 32 * G };
        var half = new MemData { AvailBytes = G / 2, TotalBytes = 16 * G, CommitBytes = 1, CommitLimitBytes = 32 * G };
        for (int i = 0; i <= 6; i++) c.Compose(Snap(i, mem: low), cfg);
        // The commit line hides: the remaining "used" line moves up, the empty line is at the end of the section.
        var kept = c.Compose(Snap(8, mem: half), cfg);
        int h = kept.FindIndex(l => l.Text.Contains("memory"));
        Assert.StartsWith("used", kept[h + 1].Text);
        Assert.True(kept[h + 2].IsPadding);
    }

    [Fact]
    public void HeightIsKept_Inline_KeepsHeaderAndPlace()
    {
        var c = new StatusComposer();
        var cfg = Cfg(hold: 30);
        cfg.Layout.KeptSpace = "inline";
        cfg.Intervals.HideDelaySeconds = 0;
        const ulong G = 1024UL * 1024 * 1024;
        var low = new MemData { AvailBytes = G / 2, TotalBytes = 16 * G, CommitBytes = 1, CommitLimitBytes = 32 * G };
        var ok = new MemData { AvailBytes = 8 * G, TotalBytes = 16 * G, CommitBytes = 1, CommitLimitBytes = 32 * G };
        for (int i = 0; i <= 6; i++) c.Compose(Snap(i, mem: low), cfg);
        var kept = c.Compose(Snap(8, mem: ok), cfg);
        int h = kept.FindIndex(l => l.Text.Contains("memory"));
        Assert.True(h > 0);
        Assert.True(kept[h + 1].IsPadding);
    }

    [Fact]
    public void DecisionTrace_ReportsShowAndHeight()
    {
        var c = new StatusComposer();
        var log = new List<string>();
        c.Trace = log.Add;
        var cfg = Cfg();
        const ulong G = 1024UL * 1024 * 1024;
        var low = new MemData { AvailBytes = G / 2, TotalBytes = 16 * G, CommitBytes = 1, CommitLimitBytes = 32 * G };
        for (int i = 0; i <= 6; i++) c.Compose(Snap(i, mem: low), cfg);
        Assert.Contains(log, m => m.StartsWith("mem.avail: over its threshold"));
        Assert.Contains(log, m => m == "show mem.avail");
        Assert.Contains(log, m => m.StartsWith("widget height"));
    }

    [Fact]
    public void FreeSpace_LowGbIsAtLeastWarning()
    {
        var c = new StatusComposer();
        var cfg = Cfg();
        const double G = 1024.0 * 1024 * 1024;
        var vols = new List<VolumeData> { new("D:\\", 15 * G, 100 * G) };
        List<Line> lines = new();
        for (int i = 0; i <= 5; i++) lines = c.Compose(Snap(i, vols: vols), cfg);
        var line = lines.Single(l => l.Text.StartsWith("D:"));
        Assert.Contains(line.Runs, r => r.Color == Rgb.Parse(cfg.Theme.Warn, Palette.Amber));
    }

    private static List<ClaudeLine> Claude() => new()
    {
        new("5h", 42, TimeSpan.FromMinutes(190), new ProjectionResult(ProjectionKind.Lasts, TimeSpan.MaxValue, TimeSpan.FromMinutes(190))),
        new("week", 18, TimeSpan.FromHours(98), new ProjectionResult(ProjectionKind.Limit, TimeSpan.FromHours(52), TimeSpan.FromHours(98))),
    };

    [Fact]
    public void Claude_BarRefreshProjection()
    {
        var cfg = Cfg();
        var lines = new StatusComposer().Compose(Snap(0, claude: Claude()), cfg);
        int h = lines.FindIndex(l => l.Text.StartsWith("\u2500\u2500 claude"));
        Assert.True(h > 0);
        Assert.StartsWith("5h   ::::::::", lines[h + 1].Text);
        Assert.EndsWith(" 42%", lines[h + 1].Text);
        // filled and empty cells differ in colour
        Assert.True(lines[h + 1].Runs.Where(r => r.Text.Contains(':')).Select(r => r.Color).Distinct().Count() >= 2);
        // Two lines per limit: bar, then refresh time and projection.
        Assert.StartsWith(" " + Icons.Refresh + "  3h10m ", lines[h + 2].Text); // a space after the icon
        Assert.EndsWith("will last", lines[h + 2].Text);
        Assert.StartsWith("week ::::", lines[h + 3].Text);
        Assert.Equal(lines[h + 1].Text.Count(ch => ch == ':'), lines[h + 3].Text.Count(ch => ch == ':')); // same bar length
        Assert.StartsWith(" " + Icons.Refresh + "  4d2h ", lines[h + 4].Text);
        // Limit at T0 (Thu 1 Oct 12:00) + 52 h = Sat 16:00; the tooltip has the details.
        Assert.EndsWith("empty Sat 16:00", lines[h + 4].Text);
        Assert.Contains(lines[h + 4].Hits, x => x.Tooltip.Contains("Runs out Sat 3 Oct 16:00") && x.Tooltip.Contains("1d22h before the reset"));
    }

    [Theory]
    [InlineData("squares", '\u25A0')]
    [InlineData("pips", '\u2022')]
    [InlineData("braille", '\u28FF')]
    [InlineData("line", '\u2501')]
    [InlineData("shade", '\u2588')]
    public void Claude_BarStyles(string style, char on)
    {
        var cfg = Cfg();
        cfg.Theme.BarStyle = style;
        var lines = new StatusComposer().Compose(Snap(0, claude: Claude()), cfg);
        Assert.Contains(lines, l => l.Text.StartsWith("5h   " + on));
    }

    [Fact]
    public void Claude_HiddenWhenNull()
        => Assert.DoesNotContain(new StatusComposer().Compose(Snap(0, claude: null), Cfg()), l => l.Text.Contains("claude"));

    [Fact]
    public void Gpu_ModelLabel_LoadAndTemp()
    {
        var c = new StatusComposer();
        var gpus = new List<GpuData>
        {
            new() { Luid = "0x0_0x1", Name = "NVIDIA GeForce RTX 4090", Load = 64, Temp = 71, Tops = Procs((7, "game.exe", 58)) },
        };
        List<Line> lines = new();
        for (int i = 0; i <= 5; i++) lines = c.Compose(Snap(i, gpus: gpus), Cfg());
        int h = lines.FindIndex(l => l.Text.StartsWith("\u2500\u2500 gpu"));
        // Temperature (shown when hot), graph, load at the right.
        Assert.StartsWith("4090  71\u00B0 ", lines[h + 1].Text);
        Assert.EndsWith("  64%", lines[h + 1].Text);
        Assert.Equal(7, lines[h + 2].Pid);
    }

    [Fact]
    public void Gpu_VramHiddenForIntegrated()
    {
        var c = new StatusComposer();
        var cfg = Cfg();
        cfg.Entries.First(e => e.Id == "gpu.vram").Enabled = true;
        const double G = 1024.0 * 1024 * 1024;
        var gpus = new List<GpuData>
        {
            new() { Luid = "a", Name = "Intel UHD", Integrated = true, Load = 0, VramUsedBytes = 0.99 * G, VramTotalBytes = G },
            new() { Luid = "b", Name = "RTX 4080", Load = 0, VramUsedBytes = 7.8 * G, VramTotalBytes = 8 * G },
        };
        List<Line> lines = new();
        for (int i = 0; i <= 5; i++) lines = c.Compose(Snap(i, gpus: gpus), cfg);
        Assert.Single(lines, l => l.Text.StartsWith("vram"));
        Assert.Contains(lines, l => l.Text.StartsWith("vram 7.8G / 8.0G"));
    }

    [Fact]
    public void Layout_Reorder_And_Spacers()
    {
        var cfg = Cfg();
        var down = cfg.Entries.First(e => e.Id == "net.down");
        cfg.Entries.Remove(down);
        cfg.Entries.Insert(0, down);
        cfg.Entries.Insert(1, LayoutEntry.Spacer("custom", "~ hello ~", collapse: false));
        var lines = new StatusComposer().Compose(Snap(0), cfg);
        Assert.StartsWith("\u2193", lines[0].Text);
        Assert.StartsWith("~ hello ~", lines[1].Text);
        Assert.StartsWith("cpu", lines[2].Text);
    }

    [Fact]
    public void Layout_DisabledItemsAreHidden()
    {
        var cfg = Cfg();
        foreach (var e in cfg.Entries) e.Enabled = false;
        Assert.Empty(new StatusComposer().Compose(Snap(0), cfg));
    }

    [Fact]
    public void Toolbar_Buttons()
    {
        var cfg = new EmberSettings();
        var ctx = new ComposeContext { MinimizedNotes = new[] { ("n1", "Shopping") } };
        var lines = new StatusComposer().Compose(Snap(0), cfg, ctx);
        var bar = lines[0];
        Assert.Contains(bar.Hits, h => h.Action == HitAction.NewNote && h.Col == 0);
        Assert.Contains(bar.Hits, h => h.Action == HitAction.RestoreNote && h.Arg == "n1" && h.Tooltip == "Shopping");
        Assert.Contains(bar.Hits, h => h.Action == HitAction.HideWidget && h.Col == Layout.Width - 2);
    }

    [Fact]
    public void PreviewAll_ShowsEverythingAtOnce()
    {
        var cfg = new EmberSettings { PreviewAll = true };
        foreach (var e in cfg.Entries) e.Enabled = true;
        var lines = new StatusComposer().Compose(new Snapshot { Time = T0 }, cfg);
        string all = string.Join("\n", lines.Select(l => l.Text));
        foreach (var h in new[] { "gpu", "memory", "disk", "claude" }) Assert.Contains(h, all);
        Assert.Contains("throttled", all);
        Assert.Contains("vram", all);
        Assert.Contains("loss", all);
        Assert.Contains(lines, l => l.Pid != 0);
        Assert.All(lines, l => Assert.Equal(Layout.Width, l.Length));
    }

    [Fact]
    public void GraphCells_ColouredByIntensity()
    {
        var c = new StatusComposer();
        var cfg = Cfg();
        List<Line> lines = new();
        for (int i = 0; i < 24; i++) lines = c.Compose(Snap(i, load: i < 12 ? 2 : 98), cfg);
        var graphRuns = lines[0].Runs.Where(r => r.Text.Any(ch => ch >= '\u2800' && ch <= '\u28FF')).ToList();
        Assert.True(graphRuns.Select(r => r.Color).Distinct().Count() >= 2);
    }
}
