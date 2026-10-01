using Ember.Core.Render;

namespace Ember.Tests;

public class GlyphTests
{
    [Fact]
    public void BrailleChar_BaselineIsBottomDots()
    {
        Assert.Equal('⣀', Glyphs.BrailleChar(1, 1)); // ⣀
        Assert.Equal('⣿', Glyphs.BrailleChar(4, 4)); // ⣿
        Assert.Equal('⠀', Glyphs.BrailleChar(0, 0));
        Assert.Equal('⣤', Glyphs.BrailleChar(2, 2)); // ⣤
        Assert.Equal('⣶', Glyphs.BrailleChar(3, 3)); // ⣶
    }

    [Theory]
    [InlineData(0, 100, 1)]
    [InlineData(-5, 100, 1)]
    [InlineData(10, 100, 1)]
    [InlineData(20, 100, 2)]
    [InlineData(50, 100, 3)]
    [InlineData(100, 100, 4)]
    [InlineData(250, 100, 4)]
    public void Level_MapsToFourLevels(double v, double scale, int expected)
        => Assert.Equal(expected, Glyphs.Level(v, scale));

    [Fact]
    public void Graph_HasFixedWidth_NewestRight_BlankForMissing()
    {
        var (text, max) = Glyphs.Graph(new double[] { 100, 100 }, 12, 100);
        Assert.Equal(12, text.Length);
        Assert.Equal('⣿', text[11]);
        Assert.Equal('⠀', text[0]);
        Assert.True(double.IsNaN(max[0]));
        Assert.Equal(100, max[11]);
    }

    [Fact]
    public void Graph_TwoSamplesPerChar()
    {
        var samples = Enumerable.Repeat(0.0, 24).ToList();
        samples[23] = 100; // right column of last char
        var (text, _) = Glyphs.Graph(samples, 12, 100);
        Assert.Equal(Glyphs.BrailleChar(1, 4), text[11]);
        Assert.All(text[..11], c => Assert.Equal('⣀', c));
    }

    [Theory]
    [InlineData(0, 4, "    ")]
    [InlineData(1, 4, "████")]
    [InlineData(0.5, 4, "██  ")]
    [InlineData(0.125 / 4, 4, "▏   ")]
    [InlineData(0.42, 20, "████████▍           ")]
    public void Bar_EighthSteps(double f, int width, string expected)
        => Assert.Equal(expected, Glyphs.Bar(f, width));

    [Fact]
    public void Bar_ClampsAndKeepsWidth()
    {
        Assert.Equal(10, Glyphs.Bar(5, 10).Length);
        Assert.Equal(10, Glyphs.Bar(double.NaN, 10).Length);
        Assert.Equal(10, Glyphs.Bar(-1, 10).Length);
    }
}

public class GradientTests
{
    [Theory]
    [InlineData(50, 75, 90, 0)]
    [InlineData(75, 75, 90, 1)]
    [InlineData(90, 75, 90, 2)]
    [InlineData(120, 75, 90, 2)]
    [InlineData(82.5, 75, 90, 1.5)]
    [InlineData(67.5, 75, 90, 0.5)]
    public void Severity_HigherIsWorse(double v, double warn, double crit, double expected)
        => Assert.Equal(expected, Gradient.Severity(v, warn, crit), 6);

    [Theory]
    [InlineData(30, 15, 5, 0)]
    [InlineData(15, 15, 5, 1)]
    [InlineData(10, 15, 5, 1.5)]
    [InlineData(5, 15, 5, 2)]
    [InlineData(1, 15, 5, 2)]
    public void Severity_LowerIsWorse(double v, double warn, double crit, double expected)
        => Assert.Equal(expected, Gradient.Severity(v, warn, crit), 6);

    [Fact]
    public void Color_EndPoints()
    {
        Assert.Equal(Palette.Bright, Gradient.Color(0));
        Assert.Equal(Palette.Amber, Gradient.Color(1));
        Assert.Equal(Palette.Red, Gradient.Color(2));
        var mid = Gradient.Color(1.5);
        Assert.Equal(Palette.Red.R, mid.R);
        Assert.InRange(mid.G, Palette.Red.G, Palette.Amber.G);
    }

    [Fact]
    public void Severity_EqualThresholdsIsStep()
    {
        Assert.Equal(0, Gradient.Severity(9, 10, 10));
        Assert.Equal(2, Gradient.Severity(10, 10, 10));
    }

    [Fact]
    public void Rgb_Conversions()
    {
        var c = new Rgb(0x11, 0x22, 0x33);
        Assert.Equal(0xFF112233u, c.ToArgb());
        Assert.Equal(0x00332211u, c.ToColorRef());
    }
}

public class FormatTests
{
    [Theory]
    [InlineData(0, "0m")]
    [InlineData(42, "42m")]
    [InlineData(190, "3h10")]
    [InlineData(60 * 24 * 4 + 120, "4d02")]
    [InlineData(-5, "0m")]
    public void Duration(double minutes, string expected)
        => Assert.Equal(expected, Format.Duration(TimeSpan.FromMinutes(minutes)));

    [Fact]
    public void Rates_UnitFromPeak()
    {
        var u = Format.UnitFor(61e6);
        Assert.Equal("Mbps", u.Name);
        Assert.Equal(" 61.0", Format.Rate5(61e6, u));
        Assert.Equal("  2.1", Format.Rate5(2.1e6, u));
        Assert.Equal("61", Format.Peak(61e6, u));
        Assert.Equal("  612", Format.Rate5(612e6, u));
        Assert.Equal("kbps", Format.UnitFor(0).Name);
        Assert.Equal("Gbps", Format.UnitFor(2e9).Name);
    }

    [Fact]
    public void RateCompact()
    {
        Assert.Equal("61M", Format.RateCompact(61e6));
        Assert.Equal("2.1M", Format.RateCompact(2.1e6));
        Assert.Equal("0.0k", Format.RateCompact(0));
    }

    [Fact]
    public void Bytes()
    {
        Assert.Equal("1.0 GB", Format.Bytes(1024.0 * 1024 * 1024));
        Assert.Equal("512 MB", Format.Bytes(512.0 * 1024 * 1024));
        Assert.Equal("45.0 MB/s", Format.BytesPerSecond(45.0 * 1024 * 1024));
    }

    [Fact]
    public void Fit()
    {
        Assert.Equal("abc  ", Format.Fit("abc", 5));
        Assert.Equal("abcd…", Format.Fit("abcdefgh", 5));
    }
}

public class LineTests
{
    [Fact]
    public void Builder_PadsAndCutsToWidth()
    {
        var l = new LineBuilder().Dim("cpu").PadTo(4).Bright(" 23%").Build();
        Assert.Equal(Layout.Width, l.Length);
        Assert.StartsWith("cpu  23%", l.Text);

        var cut = new LineBuilder().Bright(new string('x', 40)).Build();
        Assert.Equal(Layout.Width, cut.Length);
    }

    [Fact]
    public void Builder_MergesSameColour()
    {
        var l = new LineBuilder().Dim("a").Dim("b").Bright("c").Build();
        Assert.Equal("ab", l.Runs[0].Text);
    }

    [Fact]
    public void Right_AlignsAtEdge()
    {
        var l = new LineBuilder().Dim("x").Right(new Run("42%", Palette.Bright)).Build();
        Assert.EndsWith("42%", l.Text);
        Assert.Equal(Layout.Width, l.Length);
    }

    [Fact]
    public void Header_IsLetterSpacedWithRule()
    {
        var h = Layout.Header("GPU");
        Assert.StartsWith("─ G P U ─", h.Text);
        Assert.Equal(Layout.Width, h.Length);
        Assert.All(h.Runs, r => Assert.Equal(Palette.Dim, r.Color));
    }

    [Fact]
    public void Key_ChangesWithColour()
    {
        var a = new LineBuilder().Add("x", Palette.Amber).Build();
        var b = new LineBuilder().Add("x", Palette.Red).Build();
        Assert.NotEqual(a.Key, b.Key);
    }
}
