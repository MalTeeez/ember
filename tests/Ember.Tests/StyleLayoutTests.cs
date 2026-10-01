using Ember.Core.Config;
using Ember.Core.Notes;
using Ember.Core.Render;
using Ember.Core.Status;
using Ember.Core.Stats;

namespace Ember.Tests;

public class Format4Tests
{
    [Theory]
    [InlineData(23, " 23%")]
    [InlineData(100, "100%")]
    [InlineData(0, "  0%")]
    public void Pct4(double v, string s) => Assert.Equal(s, Format.Pct4(v));

    [Theory]
    [InlineData(61e6, " 61M")]
    [InlineData(2.1e6, "2.1M")]
    [InlineData(612e6, "612M")]
    [InlineData(1.2e9, "1.2G")]
    [InlineData(980e3, "980k")]
    [InlineData(999.6e3, "1.0M")]
    [InlineData(0, "0.0k")]
    public void Rate4(double bps, string s) => Assert.Equal(s, Format.Rate4(bps));

    [Fact]
    public void Rate4_Bytes() => Assert.Equal("7.6M", Format.Rate4(61e6, bytes: true));

    [Theory]
    [InlineData(7.8 * 1024 * 1024 * 1024, "7.8G")]
    [InlineData(512.0 * 1024 * 1024, "512M")]
    [InlineData(24.0 * 1024 * 1024 * 1024, " 24G")]
    public void Bytes4(double b, string s) => Assert.Equal(s, Format.Bytes4(b));

    [Fact]
    public void Others()
    {
        Assert.Equal("4.8G", Format.Clock4(4800));
        Assert.Equal("800M", Format.Clock4(800));
        Assert.Equal(" 71°", Format.Temp4(71.2));
        Assert.Equal(" 980", Format.Count4(980));
        Assert.Equal("1.5k", Format.Count4(1500));
        Assert.Equal(" 2.4", Format.Num4(2.4));
        Assert.Equal(" 140", Format.Num4(140));
    }

    [Fact]
    public void AllFourWide()
    {
        foreach (var v in new[] { 0, 0.5, 9.94, 9.96, 99.4, 99.6, 999.4, 999.6, 12345.0, 1e7, 1e10 })
        {
            Assert.Equal(4, Format.Rate4(v * 1000).Length);
            Assert.Equal(4, Format.Bytes4(v * 1024 * 1024).Length);
            Assert.Equal(4, Format.Count4(v).Length);
        }
    }
}

public class BarStyleTests
{
    [Fact]
    public void StyledBar_CountsFilled()
    {
        var (text, filled) = Glyphs.StyledBar(0.5, 10, "colons");
        Assert.Equal(new string(':', 10), text);
        Assert.Equal(5, filled);
        var (p, n) = Glyphs.StyledBar(0.01, 10, "pips");
        Assert.Equal(1, n); // any use shows one cell
        Assert.StartsWith("•·", p);
        var (b, bn) = Glyphs.StyledBar(0.42, 20, "blocks");
        Assert.StartsWith("████████▍", b);
        Assert.Equal(9, bn);
        Assert.EndsWith("·", b);
    }
}

public class ThemeTests
{
    [Fact]
    public void BuiltIns_RoundTripJson()
    {
        Assert.True(ThemeStore.BuiltIn.Count >= 6);
        Assert.Equal(ThemeStore.BuiltIn.Count, ThemeStore.BuiltIn.Select(t => t.Name).Distinct().Count());
        foreach (var t in ThemeStore.BuiltIn)
        {
            var back = ThemeStore.Parse(ThemeStore.Serialize(t))!;
            Assert.Equal(ThemeStore.Serialize(t), ThemeStore.Serialize(back));
            Assert.Contains(t.Background.Kind, ThemeStore.BackgroundKinds);
            Assert.Contains(t.HeaderStyle, ThemeStore.HeaderStyles);
            Assert.Contains(t.BarStyle, ThemeStore.BarStyles);
            var c = ThemeColors.From(t);
            Assert.NotEmpty(c.GraphStops);
        }
        Assert.True(ThemeStore.BuiltIn.Select(t => t.Background.Kind).Distinct().Count() >= 5);
    }

    [Fact]
    public void Parse_BadJson() => Assert.Null(ThemeStore.Parse("{ nope"));

    [Fact]
    public void Colours_ParseAndStops()
    {
        Assert.Equal(new Rgb(0xAA, 0xBB, 0xCC), Rgb.Parse("#abc", Palette.Dim));
        Assert.Equal(Palette.Dim, Rgb.Parse("zzz", Palette.Dim));
        var stops = new[] { new Rgb(0, 0, 0), new Rgb(100, 100, 100), new Rgb(200, 200, 200) };
        Assert.Equal(new Rgb(50, 50, 50), Rgb.Stops(stops, 0.25));
        Assert.Equal(stops[2], Rgb.Stops(stops, 1));
        Assert.Equal(new Rgb(1, 2, 3), Rgb.FromColorRef(new Rgb(1, 2, 3).ToColorRef()));
    }

    [Fact]
    public void GraphCell_Modes()
    {
        var t = new Theme { GraphColoring = "mono" };
        var c = ThemeColors.From(t);
        Assert.Equal(c.Text, c.GraphCell(0.9, c.Cpu));
        t.GraphColoring = "category";
        Assert.Equal(c.Cpu, ThemeColors.From(t).GraphCell(0.9, c.Cpu));
        t.GraphColoring = "intensity";
        var i = ThemeColors.From(t);
        Assert.NotEqual(i.GraphCell(0, i.Cpu), i.GraphCell(1, i.Cpu));
    }
}

public class LayoutPresetTests
{
    [Fact]
    public void Default_HasEveryItemOnce()
    {
        var p = LayoutPreset.Default();
        var items = p.Entries.Where(e => e.Kind == "item").Select(e => e.Id).ToList();
        Assert.Equal(ItemCatalog.All.Count, items.Count);
        Assert.Equal(items.Count, items.Distinct().Count());
    }

    [Fact]
    public void Json_RoundTrip_And_Normalize()
    {
        foreach (var p in LayoutPreset.BuiltIn)
        {
            var back = LayoutPreset.Parse(p.ToJson())!;
            Assert.Equal(p.Entries.Count(e => e.Kind == "item"), back.Entries.Count(e => e.Kind == "item"));
        }
        var odd = LayoutPreset.Parse("""{"name":"x","entries":[{"kind":"item","id":"cpu.load"},{"kind":"item","id":"cpu.load"},{"kind":"item","id":"nope"},{"kind":"spacer","preset":"dots"}]}""")!;
        Assert.Single(odd.Entries, e => e.Id == "cpu.load");
        Assert.DoesNotContain(odd.Entries, e => e.Id == "nope");
        Assert.Contains(odd.Entries, e => e.Id == "gpu.load" && !e.Enabled); // missing items are added, off
        Assert.Null(LayoutPreset.Parse("[]"));
    }

    [Fact]
    public void Spacers()
    {
        var c = ThemeColors.From(new Theme());
        Assert.Equal(new string('─', Layout.Width), Layout.Spacer("rule", null, c).Text);
        Assert.StartsWith("hey", Layout.Spacer("custom", "hey", c).Text);
        Assert.Equal("", Layout.Spacer("blank", null, c).Text.Trim());
    }
}

public class StabilityTests
{
    private static readonly DateTime T0 = new(2026, 1, 1);

    [Fact]
    public void HeightKeeper_KeepsThenShrinks()
    {
        var k = new HeightKeeper();
        var hold = TimeSpan.FromSeconds(30);
        Assert.Equal(3, k.Reserve("a", 3, T0, hold));
        Assert.Equal(3, k.Reserve("a", 1, T0.AddSeconds(10), hold));
        Assert.Equal(3, k.Reserve("a", 0, T0.AddSeconds(29), hold));
        Assert.Equal(0, k.Reserve("a", 0, T0.AddSeconds(30), hold));
        Assert.Equal(2, k.Reserve("a", 2, T0.AddSeconds(31), hold));
        Assert.Equal(1, k.Reserve("b", 1, T0, TimeSpan.Zero));
    }

    [Fact]
    public void StickyTop_ChangesOnlyAfterStickTime()
    {
        var s = new StickyTop();
        var stick = TimeSpan.FromSeconds(10);
        ProcInfo P(int pid, double v) => new(pid, "p" + pid, v, v.ToString());
        Assert.Equal(1, s.Update(new[] { P(1, 50), P(2, 40) }, T0, stick)!.Pid);
        Assert.Equal(1, s.Update(new[] { P(2, 60), P(1, 30) }, T0.AddSeconds(1), stick)!.Pid);
        Assert.Equal(30, s.Update(new[] { P(2, 60), P(1, 30) }, T0.AddSeconds(5), stick)!.Value);
        Assert.Equal(2, s.Update(new[] { P(2, 60), P(1, 30) }, T0.AddSeconds(11), stick)!.Pid);
        // The shown process is gone: change at once.
        Assert.Equal(3, s.Update(new[] { P(3, 10) }, T0.AddSeconds(12), stick)!.Pid);
        // A short gap in the candidates keeps the shown process for the stick time.
        Assert.Equal(3, s.Update(null, T0.AddSeconds(13), stick)!.Pid);
        Assert.Null(s.Update(null, T0.AddSeconds(23), stick));
    }
}

public class MarkdownMoreTests
{
    private static bool Has(List<MdSpan> spans, int start, int len, MdStyle style, int level = -1)
        => spans.Any(s => s.Start == start && s.Length == len && (s.Style & style) == style && (level < 0 || s.Level == level));

    [Fact]
    public void HeadingLevels()
    {
        var s = MarkdownStyler.Style("# a\r### b");
        Assert.True(Has(s, 0, 3, MdStyle.Heading, 1));
        Assert.True(Has(s, 4, 5, MdStyle.Heading, 3));
    }

    [Fact]
    public void QuotesNested()
    {
        var s = MarkdownStyler.Style("> one\r>> two\r> > three");
        Assert.True(Has(s, 0, 5, MdStyle.Quote, 1));
        Assert.True(Has(s, 6, 6, MdStyle.Quote, 2));
        Assert.True(Has(s, 6, 3, MdStyle.Quote | MdStyle.Marker, 2));
        Assert.True(Has(s, 13, 9, MdStyle.Quote, 2));
    }

    [Fact]
    public void QuoteKeepsInlineStyles()
    {
        var s = MarkdownStyler.Style("> **b**");
        Assert.True(Has(s, 2, 5, MdStyle.Bold));
    }

    [Fact]
    public void FencedCode_NoInlineStyling()
    {
        string t = "```\r**not bold**\r```\rafter **b**";
        var s = MarkdownStyler.Style(t);
        Assert.True(Has(s, 0, 3, MdStyle.CodeBlock | MdStyle.Marker));
        Assert.True(Has(s, 4, 12, MdStyle.CodeBlock));
        Assert.DoesNotContain(s, x => x.Start >= 4 && x.Start < 16 && (x.Style & MdStyle.Bold) != 0);
        Assert.Contains(s, x => (x.Style & MdStyle.Bold) != 0 && x.Start > 20);
        var tilde = MarkdownStyler.Style("~~~\rx\r~~~");
        Assert.True(Has(tilde, 4, 1, MdStyle.CodeBlock));
    }

    [Fact]
    public void Strike()
    {
        var s = MarkdownStyler.Style("a ~~gone~~ b");
        Assert.True(Has(s, 2, 8, MdStyle.Strike));
        Assert.True(Has(s, 2, 2, MdStyle.Strike | MdStyle.Marker));
    }

    [Fact]
    public void Links()
    {
        string t = "see [docs](https://x.org/a) or https://y.com/b.";
        var s = MarkdownStyler.Style(t);
        Assert.True(Has(s, 5, 4, MdStyle.Link));
        Assert.True(Has(s, 4, 1, MdStyle.Marker));
        Assert.True(Has(s, 31, 15, MdStyle.Link));
        Assert.Equal("https://x.org/a", MarkdownStyler.LinkAt(t, 6));
        Assert.Equal("https://y.com/b", MarkdownStyler.LinkAt(t, 35));
        Assert.Null(MarkdownStyler.LinkAt(t, 1));
    }

    [Fact]
    public void HorizontalRule()
    {
        Assert.True(Has(MarkdownStyler.Style("---"), 0, 3, MdStyle.Rule));
        Assert.True(Has(MarkdownStyler.Style("* * *"), 0, 5, MdStyle.Rule));
        Assert.False(Has(MarkdownStyler.Style("- item"), 0, 6, MdStyle.Rule));
    }
}

public class CoreGridTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(6, 2)]
    [InlineData(7, 3)]
    [InlineData(9, 3)]
    public void Columns(int n, int cols) => Assert.Equal(cols, CoreGrid.Columns(n));

    [Fact]
    public void Score_HotTemperatureBeatsLoad()
    {
        double busyCool = CoreGrid.Score(90, 60, 58, 75, 90);
        double moderateHot = CoreGrid.Score(55, 92, 60, 75, 90);
        Assert.True(moderateHot > busyCool);
        Assert.Equal(40, CoreGrid.Score(40, double.NaN, 50, 75, 90));
    }

    [Fact]
    public void Select_TopNineByScore_SortedByIndex()
    {
        var hot = Enumerable.Range(0, 12).Select(i => new HotCore(i, 50 + i, double.NaN, 0, 50 + i)).ToList();
        var (shown, hidden) = CoreGrid.Select(hot);
        Assert.Equal(9, shown.Count);
        Assert.Equal(Enumerable.Range(3, 9), shown.Select(c => c.Index));
        Assert.Equal(3, hidden.Count);
        Assert.Equal(50 + 51 + 52, CoreGrid.CoreSum(hidden));
    }
}

public class CoreGridComposeTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 12, 0, 0);

    private static List<Ember.Core.Render.Line> Run(double[] cores, double[]? temps = null)
    {
        var cfg = new EmberSettings();
        cfg.Entries.First(e => e.Id == "toolbar").Enabled = false;
        cfg.Layout.HoldSeconds = 0;
        var c = new StatusComposer();
        List<Ember.Core.Render.Line> lines = new();
        for (int i = 0; i <= 6; i++)
            lines = c.Compose(new Snapshot
            {
                Time = T0.AddSeconds(i),
                Cpu = new CpuData { Load = cores.Average(), CoreLoads = cores, CoreTemps = temps, Perf = 100 },
            }, cfg);
        return lines;
    }

    [Fact]
    public void FiveHotCores_TwoColumns_ThreeRows()
    {
        var cores = new double[16];
        for (int i = 0; i < 5; i++) cores[i] = 95;
        var lines = Run(cores);
        var grid = lines.Where(l => l.Text.StartsWith(" c")).ToList();
        Assert.Equal(3, grid.Count);
        // 2 columns of 15 with 1 gap, aligned right (indent 1).
        Assert.StartsWith(" c0 ", grid[0].Text);
        Assert.Equal("c3 ", grid[0].Text.Substring(17, 3));
        Assert.Contains(" 95%", grid[0].Text);
        Assert.All(grid, l => Assert.Equal(Layout.Width, l.Length));
    }

    [Fact]
    public void TwelveHotCores_ThreeColumns_PlusHiddenLine()
    {
        var cores = new double[16];
        for (int i = 0; i < 12; i++) cores[i] = 90;
        var lines = Run(cores);
        var grid = lines.Where(l => l.Text.StartsWith("   c")).ToList();
        Assert.Equal(3, grid.Count);
        // 3 columns of 9 with 1 gap, indent 3.
        Assert.Equal("c3 ", grid[0].Text.Substring(13, 3));
        Assert.Contains(" 90%", grid[0].Text);
        Assert.Contains(lines, l => l.Text.Contains("3 hot cores hidden, at ") && l.Text.Contains("%"));
    }

    [Fact]
    public void HotTemperatureCore_IsShownAtModerateLoad()
    {
        var cores = Enumerable.Repeat(20.0, 8).ToArray();
        var temps = Enumerable.Repeat(55.0, 8).ToArray();
        temps[5] = 80;
        var lines = Run(cores, temps);
        Assert.Contains(lines, l => l.Text.TrimStart().StartsWith("c5 "));
    }

    [Fact]
    public void TopRow_IsDragHandle()
    {
        var cfg = new EmberSettings();
        var lines = new StatusComposer().Compose(new Snapshot { Time = T0, Cpu = new CpuData { Load = 5 } }, cfg);
        Assert.Contains(lines[0].Hits, h => h.Action == HitAction.Drag && h.Width == Layout.Width);
        Assert.Contains(lines[0].Hits, h => h.Action == HitAction.NewNote);
    }
}

public class HeaderAlignTests
{
    private static string H(string align, int indent, string style = "rule")
        => Layout.Header("gpu", ThemeColors.From(new Theme { HeaderStyle = style, HeaderAlign = align, HeaderIndent = indent })).Text;

    [Fact]
    public void Left_Indent()
    {
        Assert.StartsWith("gpu ─", H("left", 0));
        Assert.StartsWith("──── gpu ─", H("left", 4));
        Assert.Equal(Layout.Width, H("left", 4).Length);
    }

    [Fact]
    public void Right_And_Center()
    {
        Assert.EndsWith("─ gpu", H("right", 0));
        Assert.EndsWith(" gpu ──", H("right", 2));
        var c = H("center", 2);
        int at = c.IndexOf("gpu", StringComparison.Ordinal);
        Assert.InRange(at, 13, 16);
        Assert.Equal(Layout.Width, c.Length);
    }

    [Fact]
    public void ThemeParse_FixesBadValues()
    {
        var t = ThemeStore.Parse("""{"headerAlign":"diagonal","headerIndent":99}""")!;
        Assert.Equal("left", t.HeaderAlign);
        Assert.Equal(24, t.HeaderIndent);
    }
}

public class LayoutMigrationTests
{
    [Fact]
    public void OldLayout_GetsNetHeader_Once()
    {
        var entries = new List<LayoutEntry>
        {
            LayoutEntry.Item("cpu.load"), LayoutEntry.Item("net.down"), LayoutEntry.Item("net.up"),
            LayoutEntry.Spacer(), LayoutEntry.Header("gpu"), LayoutEntry.Item("gpu.load"),
        };
        var s = SettingsStore.Parse(System.Text.Json.JsonSerializer.Serialize(new { entries }));
        int net = s.Entries.FindIndex(e => e.Id == "net.down");
        Assert.Equal("header", s.Entries[net - 1].Kind);
        Assert.Equal("net", s.Entries[net - 1].Text);
        Assert.Equal("spacer", s.Entries[net - 2].Kind);
        Assert.Equal(LayoutPreset.CurrentVersion, s.LayoutVersion);
        // Saved and loaded again: no second header.
        var again = SettingsStore.Parse(SettingsStore.Serialize(s));
        Assert.Single(again.Entries, e => e.Kind == "header" && e.Text == "net");
    }

    [Fact]
    public void PresetChoices_ShowTheChosenName()
    {
        var s = new EmberSettings();
        var theme = SettingsSchema.Fields.First(f => f.Label == "Theme preset");
        theme.Set(s, "nord");
        Assert.Equal("nord", theme.Get(s));
        var layout = SettingsSchema.Fields.First(f => f.Label == "Load layout preset");
        layout.Set(s, "compact");
        Assert.Equal("compact", layout.Get(s));
    }
}

public class DataGateTests
{
    private static readonly DateTime T0 = new(2026, 1, 1);

    [Fact]
    public void LostData_HidesForAMinute()
    {
        var g = new DataGate();
        Assert.False(g.Update("c", false, T0));                // never had data: just empty
        Assert.True(g.Update("c", true, T0.AddSeconds(5)));     // first data shows at once
        Assert.False(g.Update("c", false, T0.AddSeconds(10)));  // lost
        Assert.False(g.Update("c", true, T0.AddSeconds(30)));   // back too soon: still hidden
        Assert.True(g.Update("c", true, T0.AddSeconds(71)));    // a minute after the loss
    }

    [Fact]
    public void Composer_UsageSectionGoesWithoutKeptSpace()
    {
        var cfg = new EmberSettings();
        cfg.Layout.HoldSeconds = 600;
        var c = new StatusComposer();
        var claude = new List<Ember.Core.Claude.ClaudeLine>
        {
            new("5h", 42, TimeSpan.FromMinutes(52), new Ember.Core.Claude.ProjectionResult(Ember.Core.Claude.ProjectionKind.Lasts, TimeSpan.MaxValue, TimeSpan.FromMinutes(52))),
        };
        var t0 = new DateTime(2026, 10, 1, 12, 0, 0);
        Assert.Contains(c.Compose(new Snapshot { Time = t0, Claude = claude }, cfg), l => l.Text.Contains("claude"));
        var gone = c.Compose(new Snapshot { Time = t0.AddSeconds(1), Claude = null }, cfg);
        Assert.DoesNotContain(gone, l => l.Text.Contains("claude"));
    }
}

public class NoteSettingsTests
{
    [Fact]
    public void NoteOpacity_OwnSetting_Clamped()
    {
        var s = SettingsStore.Parse("""{"notes":{"opacity":5},"layout":{"opacity":20}}""");
        Assert.Equal(30, s.Notes.Opacity);
        Assert.Equal(100, new EmberSettings().Notes.Opacity);
    }
}

public class RowBackgroundTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 12, 0, 0);

    [Fact]
    public void Options_AddBackgrounds()
    {
        var cfg = new EmberSettings { PreviewAll = true };
        cfg.Theme.HeaderBand = cfg.Theme.GraphTrack = cfg.Theme.BarTrack = cfg.Theme.RowStripes = cfg.Theme.ProcessBand = true;
        var lines = new StatusComposer().Compose(new Snapshot { Time = T0 }, cfg);
        Assert.Contains(lines, l => l.Text.Contains("net") && l.Back.Visible);           // header band
        Assert.Contains(lines, l => l.Runs.Any(r => r.Text.Any(ch => ch is >= '⠀' and <= '⣿') && r.Back.Visible)); // graph track
        Assert.Contains(lines, l => l.Pid > 0 && l.Back.Visible);                         // process band
        // Off by default.
        var plain = new StatusComposer().Compose(new Snapshot { Time = T0 }, new EmberSettings { PreviewAll = true });
        Assert.DoesNotContain(plain, l => l.Back.Visible || l.Runs.Any(r => r.Back.Visible));
    }

    [Fact]
    public void HeaderIcons_BeforeTitle()
    {
        var t = new Theme { HeaderIcons = true };
        var h = Layout.Header("gpu", ThemeColors.From(t));
        Assert.Contains(h.Runs, r => r.Style == RunStyle.Icon && r.Text[0] == Icons.Monitor);
        Assert.Equal(Layout.Width, h.Length);
        Assert.Contains("  gpu ", h.Text);
    }

    [Fact]
    public void BuiltInThemes_UniqueNames_RoundTrip()
    {
        var names = ThemeStore.BuiltIn.Select(t => t.Name).ToList();
        Assert.True(names.Count >= 20);
        Assert.Equal(names.Count, names.Distinct().Count());
        foreach (var t in ThemeStore.BuiltIn)
        {
            Assert.Contains(t.HeaderStyle, ThemeStore.HeaderStyles);
            Assert.Contains(t.Background.Kind, ThemeStore.BackgroundKinds);
            Assert.Contains(t.BarStyle, ThemeStore.BarStyles);
            Assert.Equal(ThemeStore.Serialize(t), ThemeStore.Serialize(ThemeStore.Parse(ThemeStore.Serialize(t))!));
        }
    }

    [Fact]
    public void CcdTemps_SplitOverCores()
    {
        // 4 physical cores, 2 threads each, 2 CCDs.
        var phys = new[] { 0, 0, 1, 1, 2, 2, 3, 3 };
        var t = CoreTempMap.ByCcd(phys, new[] { 60.0, 70.0 });
        Assert.Equal(new[] { 60.0, 60, 60, 60, 70, 70, 70, 70 }, t);
        Assert.True(double.IsNaN(CoreTempMap.ByCcd(new[] { -1 }, new[] { 60.0 })[0]));
    }

    [Fact]
    public void Formats_HzAndPackets()
    {
        Assert.Equal("4.8GHz", Format.Hz6(4800));
        Assert.Equal("800MHz", Format.Hz6(800));
        Assert.Equal("457k pkt", Format.Pkt(457_000));
    }
}

public class DiskMemoryLineTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 12, 0, 0);

    [Fact]
    public void DiskNames_Readable()
    {
        Assert.Equal("C:", Ember.Core.Util.DiskNames.Display("d0", "C:"));
        Assert.Equal("D:E:", Ember.Core.Util.DiskNames.Display("d1", "D: E:"));
        Assert.Equal("D:E:F:", Ember.Core.Util.DiskNames.Display("d1", "D: E: F: G:"));
        Assert.Equal("disk2", Ember.Core.Util.DiskNames.Display("d2", ""));
    }

    [Fact]
    public void BusyDisk_LetterMetricGraphValue()
    {
        var cfg = new EmberSettings();
        cfg.Layout.HoldSeconds = 0;
        var c = new StatusComposer();
        List<Ember.Core.Render.Line> lines = new();
        for (int i = 0; i <= 6; i++)
            lines = c.Compose(new Snapshot { Time = T0.AddSeconds(i), Disks = new() { new("d1", "C:", 100, 3, 2.6, 0.4) } }, cfg);
        var l = lines.Single(x => x.Text.StartsWith("C:"));
        Assert.StartsWith("C:  busy ", l.Text);
        Assert.EndsWith(" 100%", l.Text);
        int k = lines.IndexOf(l);
        Assert.Equal("  └ queued: 3 reads, <1 write", lines[k + 1].Text.TrimEnd());
        Assert.Contains(l.Text[9], "⠀⣿".ToCharArray().Concat(Enumerable.Range(0x2800, 256).Select(x => (char)x)));
        Assert.Equal(Layout.Width, l.Length);
    }

    [Fact]
    public void PageFaults_SpelledOut()
    {
        var cfg = new EmberSettings();
        cfg.Layout.HoldSeconds = 0;
        var c = new StatusComposer();
        const ulong G = 1024UL * 1024 * 1024;
        List<Ember.Core.Render.Line> lines = new();
        for (int i = 0; i <= 6; i++)
            lines = c.Compose(new Snapshot { Time = T0.AddSeconds(i), Mem = new MemData { AvailBytes = 8 * G, TotalBytes = 16 * G, CommitBytes = G, CommitLimitBytes = 32 * G, PagesInPerSec = 6000 } }, cfg);
        var l = lines.Single(x => x.Text.Contains("hard disk paging"));
        Assert.StartsWith("hard disk paging ", l.Text);
        Assert.EndsWith("6.0k/s", l.Text);
    }
}

public class FaultLineTests
{
    [Fact]
    public void FaultProcesses_ListedUnderPaging_WithColours()
    {
        var cfg = new EmberSettings();
        cfg.Layout.HoldSeconds = 0;
        var c = new StatusComposer();
        const ulong G = 1024UL * 1024 * 1024;
        var procs = new List<FaultProc> { new(10, "chrome.exe", 4.0 * G, 900), new(11, "code.exe", 0.2 * G, 100) };
        List<Ember.Core.Render.Line> lines = new();
        for (int i = 0; i <= 6; i++)
            lines = c.Compose(new Snapshot
            {
                Time = new DateTime(2026, 10, 1, 12, 0, i),
                Mem = new MemData { AvailBytes = 8 * G, TotalBytes = 16 * G, CommitBytes = G, CommitLimitBytes = 32 * G, PagesInPerSec = 6000 },
                FaultProcs = procs, FaultsTotal = 1000,
            }, cfg);
        Assert.True(c.Needs.Faults);
        int k = lines.FindIndex(l => l.Text.StartsWith("hard disk paging"));
        var first = lines[k + 1];
        Assert.StartsWith(" └ chrome ", first.Text); // no .exe
        Assert.EndsWith(" 4.0GB  900/s", first.Text);
        Assert.Equal(10, first.Pid);
        // chrome: a quarter of the RAM and 90 % of the faults → top colour; code.exe lower.
        var top = Rgb.Parse(cfg.Theme.GraphStops[^1], Palette.Red);
        Assert.Contains(first.Runs, r => r.Text.Contains("900/s") && r.Color == Rgb.Stops(ThemeColors.From(cfg.Theme).GraphStops, 0.9));
        Assert.Contains(first.Runs, r => r.Text.Contains("4.0GB") && r.Color == top);
        Assert.StartsWith(" └ code ", lines[k + 2].Text);
    }
}

public class QueueTextTests
{
    [Theory]
    [InlineData(2.6, 0.4, 3.0, "3 reads, <1 write")]
    [InlineData(1.0, 0, 1.0, "1 read")]
    [InlineData(0, 4.4, 4.4, "4 writes")]
    [InlineData(0, 0, 0, "none")]
    [InlineData(0, 0, 2.2, "2 requests")]
    public void Words(double r, double w, double total, string expected)
        => Assert.Equal(expected, Format.QueueText(r, w, total));
}

public class ProcessNameTests
{
    [Theory]
    [InlineData("chrome.exe", null, "chrome")]
    [InlineData("python3.13.exe", "\"C:\\Py\\python3.13.exe\" -u C:\\work\\main.py --fast", "python3.13 main.py")]
    [InlineData("node.exe", "node server.js", "node server.js")]
    [InlineData("chrome.exe", "\"C:\\Chrome\\chrome.exe\" --type=renderer --lang=en", "chrome")]
    [InlineData("svchost.exe", "C:\\Windows\\system32\\svchost.exe -k netsvcs -p", "svchost netsvcs")]
    public void Display(string name, string? cmd, string expected)
        => Assert.Equal(expected, Ember.Core.Util.ProcessNames.Display(name, cmd));

    [Fact]
    public void FitDots_CutsWithTwoDots()
    {
        Assert.Equal("python3.13 ma..", Format.FitDots("python3.13 main.py", 15));
        Assert.Equal("node  ", Format.FitDots("node", 6));
    }
}

public class SoftPagingAndPeakTests
{
    [Fact]
    public void SoftPaging_OnlyWithoutHardPaging()
    {
        var cfg = new EmberSettings();
        cfg.Layout.HoldSeconds = 0;
        const ulong G = 1024UL * 1024 * 1024;
        var procs = new List<FaultProc> { new(10, "chrome.exe", G, 0, 40000), new(11, "game.exe", G, 900, 100) };
        List<Ember.Core.Render.Line> Run(double hard, double soft)
        {
            var c = new StatusComposer();
            List<Ember.Core.Render.Line> lines = new();
            for (int i = 0; i <= 6; i++)
                lines = c.Compose(new Snapshot
                {
                    Time = new DateTime(2026, 10, 1, 12, 0, i),
                    Mem = new MemData { AvailBytes = 8 * G, TotalBytes = 16 * G, CommitBytes = G, CommitLimitBytes = 32 * G, PagesInPerSec = hard, SoftFaultsPerSec = soft },
                    FaultProcs = procs, FaultsTotal = 900, SoftFaultsTotal = 40100,
                }, cfg);
            return lines;
        }
        var softOnly = Run(10, 60000);
        int k = softOnly.FindIndex(l => l.Text.StartsWith("soft disk paging"));
        Assert.True(k >= 0);
        Assert.StartsWith(" └ chrome ", softOnly[k + 1].Text);
        var both = Run(6000, 60000);
        Assert.Contains(both, l => l.Text.StartsWith("hard disk paging"));
        Assert.DoesNotContain(both, l => l.Text.StartsWith("soft disk paging"));
    }

    [Fact]
    public void SlidingPeak_ForgetsAfterTheWindow()
    {
        var t0 = new DateTime(2026, 1, 1);
        var p = new SlidingPeak();
        p.Add(t0, 100);
        p.Add(t0.AddMinutes(10), 20);
        Assert.Equal(100, p.Max);
        Assert.Equal(0.2, p.Fraction(20), 3);
        p.Add(t0.AddMinutes(61), 30);
        Assert.Equal(30, p.Max);
    }
}

public class V6Tests
{
    [Theory]
    [InlineData("double", '═')]
    [InlineData("heavy", '━')]
    [InlineData("box", '╡')]
    [InlineData("wave", '∿')]
    [InlineData("chevron", '»')]
    public void NewHeaderStyles(string style, char ch)
    {
        var h = Layout.Header("gpu", ThemeColors.From(new Theme { HeaderStyle = style }));
        Assert.Contains(ch, h.Text);
        Assert.Contains("gpu", h.Text);
        Assert.Equal(Layout.Width, h.Length);
    }

    [Fact]
    public void PingLine_ShowsHost_Always()
    {
        var cfg = new EmberSettings();
        cfg.Entries.First(e => e.Id == "net.quality").Enabled = true;
        cfg.Network.PingAlways = true;
        cfg.Network.ShowGatewayPing = true;
        var c = new StatusComposer();
        var lines = c.Compose(new Snapshot
        {
            Time = new DateTime(2026, 10, 1, 12, 0, 0),
            Quality = new Ember.Core.Net.NetQuality(0, 0, 2, 14, true, "192.168.1.1", "1.1.1.1"),
        }, cfg);
        Assert.Contains(lines, l => l.Text.StartsWith("ping") && l.Text.Contains("1.1.1.1"));
        Assert.Contains(lines, l => l.Text.StartsWith("ping") && l.Text.Contains("192.168.1.1"));
        cfg.Network.ShowPingHost = false;
        lines = new StatusComposer().Compose(new Snapshot { Time = new DateTime(2026, 10, 1, 12, 0, 0), Quality = new Ember.Core.Net.NetQuality(0, 0, 2, 14, true, "192.168.1.1", "1.1.1.1") }, cfg);
        Assert.Contains(lines, l => l.Text.StartsWith("ping") && l.Text.TrimEnd().EndsWith("net"));
    }

    [Fact]
    public void Chips_CoverTextOnly()
    {
        var cfg = new EmberSettings { PreviewAll = true };
        cfg.Theme.ValueChips = true;
        var lines = new StatusComposer().Compose(new Snapshot { Time = new DateTime(2026, 10, 1, 12, 0, 0) }, cfg);
        var chips = lines.SelectMany(l => l.Runs).Where(r => r.Back.Visible).ToList();
        Assert.NotEmpty(chips);
        Assert.All(chips, r => Assert.Equal(r.Text.Trim(), r.Text));
    }

    [Fact]
    public void CpuTemperature_WhenHot_ElseWiderGraph()
    {
        var snap = new Snapshot { Time = new DateTime(2026, 10, 1, 12, 0, 0), Cpu = new CpuData { Load = 5, Temp = 48 } };
        var normal = new StatusComposer().Compose(snap, new EmberSettings()).First(l => l.Text.StartsWith("cpu")).Text;
        Assert.DoesNotContain("°", normal);
        Assert.InRange(normal[5], '\u2800', '\u28FF'); // graph right after the label
        var cfg = new EmberSettings();
        cfg.Fields.CpuTempAlways = true;
        Assert.StartsWith("cpu   48°", new StatusComposer().Compose(snap, cfg).First(l => l.Text.StartsWith("cpu")).Text);
    }

    [Fact]
    public void Traceroute_CollapsesSilentHops()
    {
        var hops = new List<Ember.Core.Net.TraceHop> { new(1, "192.168.1.1", 1), new(2, null, null), new(3, null, null), new(4, "10.0.0.1", 9), new(5, "1.1.1.1", 12) };
        var c = Ember.Core.Net.Traceroute.Collapse(hops, "1.1.1.1");
        Assert.Equal(new[] { 1, 4, 5 }, c.Select(x => x.Hop.Ttl));
        Assert.Equal(2, c[1].Skipped);
        var cfg = new EmberSettings();
        cfg.Entries.First(e => e.Id == "net.quality").Enabled = true;
        cfg.Network.PingAlways = cfg.Network.ShowTraceroute = true;
        var lines = new StatusComposer().Compose(new Snapshot { Time = new DateTime(2026, 10, 1, 12, 0, 0), Quality = new Ember.Core.Net.NetQuality(0, 0, 1, 12, true, "192.168.1.1", "1.1.1.1") { Hops = hops } }, cfg);
        Assert.Contains(lines, l => l.Text.StartsWith("  └ * 4 10.0.0.1") && l.Hits.Any(h => h.Tooltip.Contains("2 hops before")));
    }

    [Fact]
    public void Layout_V3_MovesTopProcessesAndKeptSpace()
    {
        var entries = LayoutPreset.Default().Entries;
        int top = entries.FindIndex(e => e.Id == "mem.top"), pf = entries.FindIndex(e => e.Id == "mem.pagein");
        Assert.True(top < pf);
        var old = new EmberSettings();
        old.Entries.Remove(old.Entries.First(e => e.Id == "mem.top"));
        old.Entries.Add(LayoutEntry.Item("mem.top"));
        old.LayoutVersion = 2;
        old.Layout.KeptSpace = "bottom";
        var s = SettingsStore.Parse(SettingsStore.Serialize(old));
        Assert.Equal("section", s.Layout.KeptSpace);
        Assert.True(s.Entries.FindIndex(e => e.Id == "mem.top") < s.Entries.FindIndex(e => e.Id == "mem.pagein"));
    }

    [Fact]
    public void DeviceNames_BrandAndKind()
    {
        var list = new List<Ember.Core.Devices.DeviceBattery>
        {
            new("DeathAdder V3 HyperSpeed", 90, null, "razer"),
            new("WH-1000XM5 noise cancelling", 50, null, "bluetooth"),
            new("Logitech MX Master 3S for Business", 70, null, "bluetooth"),
            new("Logitech MX Anywhere 3 for Business", 70, null, "bluetooth"),
        };
        var n = Ember.Core.Devices.DeviceNames.Display(list, 16);
        Assert.Equal(new[] { "Razer Mouse", "Sony Headset", "Logitech Mouse 1", "Logitech Mouse 2" }, n);
    }
}

public class V8Tests
{
    [Fact]
    public void NewSettings_ReleaseKeptSpace()
    {
        var cfg = new EmberSettings();
        cfg.Layout.HoldSeconds = 600;
        cfg.Entries.First(e => e.Id == "net.quality").Enabled = true;
        cfg.Network.PingAlways = cfg.Network.ShowTraceroute = true;
        var hops = new List<Ember.Core.Net.TraceHop> { new(1, "192.168.1.1", 1), new(2, "1.1.1.1", 9) };
        var q = new Ember.Core.Net.NetQuality(0, 0, 1, 12, true, "192.168.1.1", "1.1.1.1") { Hops = hops };
        var c = new StatusComposer();
        var t0 = new DateTime(2026, 10, 1, 12, 0, 0);
        int with = c.Compose(new Snapshot { Time = t0, Quality = q }, cfg).Count;
        var off = SettingsStore.Clone(cfg);
        off.Network.ShowTraceroute = false;
        int without = c.Compose(new Snapshot { Time = t0.AddSeconds(1), Quality = q }, off).Count;
        Assert.Equal(with - 2, without);
    }
}
