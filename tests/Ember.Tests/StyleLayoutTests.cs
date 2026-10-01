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
        Assert.Null(s.Update(null, T0.AddSeconds(13), stick));
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
        var grid = lines.Where(l => l.Text.StartsWith("c") && !l.Text.StartsWith("cpu")).ToList();
        Assert.Equal(3, grid.Count);
        Assert.StartsWith("c0 ", grid[0].Text);
        Assert.Equal("c3 ", grid[0].Text.Substring(16, 3));
        Assert.All(grid, l => Assert.Equal(Layout.Width, l.Length));
    }

    [Fact]
    public void TwelveHotCores_ThreeColumns_PlusHiddenLine()
    {
        var cores = new double[16];
        for (int i = 0; i < 12; i++) cores[i] = 90;
        var lines = Run(cores);
        var grid = lines.Where(l => l.Text.StartsWith("c") && !l.Text.StartsWith("cpu")).ToList();
        Assert.Equal(3, grid.Count);
        Assert.Equal("c3 ", grid[0].Text.Substring(11, 3));
        Assert.Contains(lines, l => l.Text.Contains("3 hot cores hidden, at ") && l.Text.Contains("%"));
    }

    [Fact]
    public void HotTemperatureCore_IsShownAtModerateLoad()
    {
        var cores = Enumerable.Repeat(20.0, 8).ToArray();
        var temps = Enumerable.Repeat(55.0, 8).ToArray();
        temps[5] = 80;
        var lines = Run(cores, temps);
        Assert.Contains(lines, l => l.Text.StartsWith("c5 "));
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
