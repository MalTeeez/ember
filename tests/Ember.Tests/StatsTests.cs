using Ember.Core.Stats;

namespace Ember.Tests;

public class HysteresisTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0);
    private static readonly TimeSpan Show = TimeSpan.FromSeconds(5), Hide = TimeSpan.FromSeconds(10);

    [Fact]
    public void ShowsAfter5s_HidesAfter10s()
    {
        var h = new Hysteresis();
        Assert.False(h.Update(true, T0, Show, Hide));
        Assert.False(h.Update(true, T0.AddSeconds(4), Show, Hide));
        Assert.True(h.Update(true, T0.AddSeconds(5), Show, Hide));
        Assert.True(h.Update(false, T0.AddSeconds(6), Show, Hide));
        Assert.True(h.Update(false, T0.AddSeconds(15), Show, Hide));
        Assert.False(h.Update(false, T0.AddSeconds(16), Show, Hide));
    }

    [Fact]
    public void FlickerDoesNotShow()
    {
        var h = new Hysteresis();
        for (int i = 0; i < 30; i++)
            Assert.False(h.Update(i % 4 != 3, T0.AddSeconds(i), Show, Hide)); // false every 4th second resets the timer
    }

    [Fact]
    public void ShortDipDoesNotHide()
    {
        var h = new Hysteresis();
        h.Update(true, T0, Show, Hide);
        h.Update(true, T0.AddSeconds(5), Show, Hide);
        Assert.True(h.Update(false, T0.AddSeconds(6), Show, Hide));
        Assert.True(h.Update(true, T0.AddSeconds(14), Show, Hide));
        Assert.True(h.Update(false, T0.AddSeconds(20), Show, Hide));
        Assert.True(h.Update(false, T0.AddSeconds(29), Show, Hide));
    }

    [Fact]
    public void PendingWhileWaitingToShow()
    {
        var h = new Hysteresis();
        h.Update(true, T0, Show, Hide);
        Assert.True(h.Pending);
        Assert.False(h.Visible);
    }

    [Fact]
    public void VisibilitySet_RemovesUntouchedKeys()
    {
        var v = new VisibilitySet { ShowDelay = TimeSpan.Zero };
        v.BeginTick();
        Assert.True(v.Update("a", true, T0));
        v.EndTick();
        v.BeginTick();
        v.EndTick(); // source failed: not updated
        Assert.Equal(0, v.Count);
        v.BeginTick();
        Assert.True(v.Update("a", true, T0.AddSeconds(1)));
        v.EndTick();
    }
}

public class OutlierTests
{
    [Fact]
    public void MedianAndSigma()
    {
        Assert.Equal(2, OutlierStats.Median(new double[] { 3, 1, 2 }));
        Assert.Equal(2.5, OutlierStats.Median(new double[] { 1, 2, 3, 4 }));
        Assert.Equal(2, OutlierStats.Sigma(new double[] { 2, 4, 4, 4, 5, 5, 7, 9 }), 9);
    }

    [Fact]
    public void FindsSingleHotCore()
    {
        var loads = new double[] { 10, 10, 10, 10, 10, 10, 10, 100 };
        var o = OutlierStats.Find(loads, null, 10, 2);
        Assert.Single(o);
        Assert.Equal(7, o[0].Index);
        Assert.Equal(10, o[0].Median);
    }

    [Fact]
    public void NoOutlierWhenSigmaSmall()
    {
        var loads = new double[] { 10, 12, 11, 13, 10, 12, 11, 30 };
        Assert.Empty(OutlierStats.Find(loads, null, 10, 2));
    }

    [Fact]
    public void EvenLoadHasNoOutliers()
    {
        var loads = Enumerable.Repeat(90.0, 16).ToArray();
        Assert.Empty(OutlierStats.Find(loads, null, 10, 2));
    }

    [Fact]
    public void HybridGroupsSeparately()
    {
        // P-cores (group 1) all busy, E-cores (group 0) idle: no outliers inside each group.
        var loads = new double[] { 95, 95, 95, 95, 5, 5, 5, 5, 5, 5, 5, 5 };
        var groups = new[] { 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0 };
        Assert.Empty(OutlierStats.Find(loads, groups, 10, 2));
        // One-group calculation would differ:
        Assert.NotNull(OutlierStats.Find(loads, null, 10, 2));

        // A hot E-core is an outlier among E-cores.
        loads[11] = 100;
        var o = OutlierStats.Find(loads, groups, 10, 2);
        Assert.Single(o);
        Assert.Equal(11, o[0].Index);
    }

    [Fact]
    public void Smoother_Ema3s()
    {
        var s = new CoreSmoother();
        var t = new DateTime(2026, 1, 1);
        Assert.Equal(0, s.Update(new double[] { 0 }, t)[0]);
        var v = s.Update(new double[] { 100 }, t.AddSeconds(3))[0];
        Assert.Equal(100 * (1 - Math.Exp(-1)), v, 6);
    }

    [Fact]
    public void History_KeepsCapacity()
    {
        var h = new History(3);
        foreach (var x in new double[] { 1, 2, 3, 4 }) h.Add(x);
        Assert.Equal(new double[] { 2, 3, 4 }, h.Values);
        Assert.Equal(4, h.Max);
    }
}
