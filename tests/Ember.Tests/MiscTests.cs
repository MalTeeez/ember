using Ember.Core.Config;
using Ember.Core.Notes;
using Ember.Core.Util;

namespace Ember.Tests;

public class MarkdownTests
{
    private static bool Has(List<MdSpan> spans, int start, int len, MdStyle style)
        => spans.Any(s => s.Start == start && s.Length == len && (s.Style & style) == style);

    [Fact]
    public void Heading_MarkerDim()
    {
        var s = MarkdownStyler.Style("## Title");
        Assert.True(Has(s, 0, 8, MdStyle.Heading));
        Assert.True(Has(s, 0, 2, MdStyle.Marker));
    }

    [Fact]
    public void Bold_Italic_Code()
    {
        string t = "a **b** *c* `d` _e_";
        var s = MarkdownStyler.Style(t);
        Assert.True(Has(s, 2, 5, MdStyle.Bold));
        Assert.True(Has(s, 2, 2, MdStyle.Bold | MdStyle.Marker));
        Assert.True(Has(s, 5, 2, MdStyle.Bold | MdStyle.Marker));
        Assert.True(Has(s, 8, 3, MdStyle.Italic));
        Assert.True(Has(s, 12, 3, MdStyle.Code));
        Assert.True(Has(s, 16, 3, MdStyle.Italic));
        Assert.DoesNotContain(s, x => (x.Style & MdStyle.Bold) != 0 && (x.Style & MdStyle.Italic) != 0);
    }

    [Fact]
    public void NoStylingInsideCode()
    {
        var s = MarkdownStyler.Style("`**x**`");
        Assert.DoesNotContain(s, x => (x.Style & MdStyle.Bold) != 0);
    }

    [Fact]
    public void SnakeCaseIsNotItalic()
    {
        var s = MarkdownStyler.Style("my_var_name");
        Assert.DoesNotContain(s, x => (x.Style & MdStyle.Italic) != 0);
    }

    [Fact]
    public void Unclosed_NotStyled()
    {
        Assert.Empty(MarkdownStyler.Style("a **b"));
        Assert.Empty(MarkdownStyler.Style("2 * 3 = 6"));
    }

    [Fact]
    public void Lists_And_Checkboxes_MultiLine()
    {
        string t = "- item\r1. first\r- [ ] todo\r- [x] done";
        var s = MarkdownStyler.Style(t);
        Assert.True(Has(s, 0, 1, MdStyle.Marker));
        Assert.True(Has(s, 7, 2, MdStyle.Marker)); // "1."
        int todo = t.IndexOf("- [ ]", StringComparison.Ordinal);
        Assert.True(Has(s, todo, 6, MdStyle.Marker));
        int done = t.IndexOf("- [x]", StringComparison.Ordinal);
        Assert.True(Has(s, done + 6, 4, MdStyle.Done));
    }

    [Fact]
    public void CrLfOffsetsCountBothCharacters()
    {
        string t = "x\r\n**b**";
        var s = MarkdownStyler.Style(t);
        Assert.True(Has(s, 3, 5, MdStyle.Bold));
    }

    [Fact]
    public void CheckboxClickAndToggle()
    {
        string t = "text\r- [ ] todo";
        int open = t.IndexOf('[');
        Assert.Equal(open + 1, MarkdownStyler.CheckboxAt(t, open));
        Assert.Equal(open + 1, MarkdownStyler.CheckboxAt(t, open + 2));
        Assert.Equal(-1, MarkdownStyler.CheckboxAt(t, open + 5));
        Assert.Equal(-1, MarkdownStyler.CheckboxAt(t, 1));
        string toggled = MarkdownStyler.Toggle(t, open + 1);
        Assert.Equal("text\r- [x] todo", toggled);
        Assert.Equal(t, MarkdownStyler.Toggle(toggled, open + 1));
    }

    [Fact]
    public void CheckboxAt_FirstLineAndIndented()
    {
        Assert.Equal(3, MarkdownStyler.CheckboxAt("- [ ] a", 3));
        Assert.Equal(5, MarkdownStyler.CheckboxAt("  - [x] a", 5));
    }
}

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+N", Hotkey.ModControl | Hotkey.ModAlt, (uint)'N')]
    [InlineData("win+shift+f5", Hotkey.ModWin | Hotkey.ModShift, 0x74u)]
    [InlineData("Ctrl + 1", Hotkey.ModControl, (uint)'1')]
    public void Parses(string text, uint mods, uint vk)
    {
        Assert.True(Hotkey.TryParse(text, out var m, out var k));
        Assert.Equal(mods, m);
        Assert.Equal(vk, k);
    }

    [Theory]
    [InlineData("")]
    [InlineData("N")]
    [InlineData("Ctrl+")]
    [InlineData("Hyper+N")]
    [InlineData("Ctrl+NN")]
    public void Rejects(string text) => Assert.False(Hotkey.TryParse(text, out _, out _));
}

public class SettingsTests
{
    [Fact]
    public void Defaults_MatchSpec()
    {
        var s = new EmberSettings();
        Assert.False(s.Items.GpuVram);
        Assert.False(s.Items.NetworkQuality);
        Assert.True(s.Items.CpuTemp);
        Assert.Equal(1, s.Intervals.SensorSeconds);
        Assert.Equal(60, s.Intervals.ClaudeSeconds);
        Assert.Equal(75, s.Thresholds.CpuTemp.Warn);
        Assert.Equal(90, s.Thresholds.CpuTemp.Crit);
        Assert.Equal(15, s.Thresholds.AvailableRam.Warn);
        Assert.Equal(5, s.Thresholds.AvailableRam.Crit);
        Assert.Contains("WireGuard", s.Network.ExcludedAdapters);
        Assert.False(s.DebugLog);
    }

    [Fact]
    public void JsonRoundTrip()
    {
        var s = new EmberSettings();
        s.Items.GpuVram = true;
        s.Thresholds.GpuTemp.Warn = 66;
        s.Layout.Corner = Corner.BottomLeft;
        var back = SettingsStore.Parse(SettingsStore.Serialize(s));
        Assert.True(back.Items.GpuVram);
        Assert.Equal(66, back.Thresholds.GpuTemp.Warn);
        Assert.Equal(Corner.BottomLeft, back.Layout.Corner);
        Assert.Contains("\"BottomLeft\"", SettingsStore.Serialize(s));
    }

    [Fact]
    public void BrokenJson_GivesDefaults_AndNormalizes()
    {
        Assert.True(SettingsStore.Parse("{ broken").Items.CpuLoad);
        var s = SettingsStore.Parse("""{"Intervals":{"SensorSeconds":0},"Layout":{"Opacity":500}}""");
        Assert.Equal(0.25, s.Intervals.SensorSeconds);
        Assert.Equal(100, s.Layout.Opacity);
    }

    [Fact]
    public void SaveAndLoad()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ember-test-" + Guid.NewGuid());
        var path = Path.Combine(dir, "settings.json");
        try
        {
            var s = new EmberSettings();
            s.Notes.NewNoteHotkey = "Ctrl+Shift+M";
            SettingsStore.Save(path, s);
            Assert.Equal("Ctrl+Shift+M", SettingsStore.Load(path).Notes.NewNoteHotkey);
            Assert.True(SettingsStore.Load(Path.Combine(dir, "missing.json")).Items.Claude);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Schema_AllFieldsRoundTrip()
    {
        var s = new EmberSettings();
        foreach (var f in SettingsSchema.Fields.Where(f => !f.IsSpecial))
        {
            string v = f.Get(s);
            Assert.True(f.Set(s, v), f.Label);
            Assert.Equal(v, f.Get(s));
        }
    }

    [Fact]
    public void Schema_ValidatesNumbersAndHotkey()
    {
        var s = new EmberSettings();
        var temp = SettingsSchema.Fields.First(f => f.Label == "CPU temperature °C warning");
        Assert.False(temp.Set(s, "abc"));
        Assert.False(temp.Set(s, "999"));
        Assert.True(temp.Set(s, "80,5"));
        Assert.Equal(80.5, s.Thresholds.CpuTemp.Warn);
        var hk = SettingsSchema.Fields.First(f => f.Label == "New note hotkey");
        Assert.False(hk.Set(s, "Q"));
        Assert.True(hk.Set(s, "Ctrl+Alt+J"));
        Assert.True(hk.Set(s, ""));
        var list = SettingsSchema.Fields.First(f => f.Kind == FieldKind.List);
        Assert.True(list.Set(s, "A\r\nB\r\n\r\na"));
        Assert.Equal(new[] { "A", "B" }, s.Network.ExcludedAdapters);
    }

    [Fact]
    public void Schema_HasEveryItemToggle()
    {
        int bools = typeof(ItemSettings).GetProperties().Length;
        Assert.Equal(bools, SettingsSchema.Fields.Count(f => f.Section == "Items"));
    }

    [Fact]
    public void Threshold_Crossed()
    {
        Assert.True(new Threshold(75, 90).Crossed(75));
        Assert.False(new Threshold(75, 90).Crossed(74.9));
        Assert.True(new Threshold(15, 5).Crossed(14));
        Assert.False(new Threshold(15, 5).Crossed(15));
    }
}

public class UtilTests
{
    [Fact]
    public void ProcessTree_Descendants()
    {
        var all = new[]
        {
            new ProcEntry(1, 0, 0), new ProcEntry(10, 1, 5), new ProcEntry(11, 10, 6),
            new ProcEntry(12, 10, 7), new ProcEntry(13, 11, 8), new ProcEntry(20, 1, 9),
            new ProcEntry(30, 10, 1), // created before its "parent": PID reuse, not a child
        };
        var d = ProcessTree.Descendants(10, all);
        Assert.Equal(new[] { 11, 12, 13 }, d.OrderBy(x => x));
        Assert.Empty(ProcessTree.Descendants(13, all));
    }

    [Fact]
    public void ProcessTree_NoInfiniteLoop()
    {
        var all = new[] { new ProcEntry(1, 2, 0), new ProcEntry(2, 1, 0) };
        Assert.Equal(new[] { 2 }, ProcessTree.Descendants(1, all));
    }

    [Fact]
    public void DiskAndCpuNames()
    {
        Assert.Equal(("d0", "C:"), DiskNames.Parse("0 C:"));
        Assert.Equal(("d1", "D: E:"), DiskNames.Parse("1 D: E:"));
        Assert.Null(DiskNames.Parse("_Total"));
        Assert.Equal((0, 7), CpuNames.Parse("0,7"));
        Assert.Null(CpuNames.Parse("0,_Total"));
        Assert.Null(CpuNames.Parse("_Total"));
    }
}
