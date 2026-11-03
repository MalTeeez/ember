using Ember.Core.Claude;

namespace Ember.Tests;

public class ClaudeProjectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SpecExample_50PercentAfter1h_LimitIn1h_Red()
    {
        // 50% used after 1 h of 5 h: reset in 4 h, limit in 1 h.
        var p = Projection.Compute(50, Now.AddHours(4), Now, Projection.FiveHours);
        Assert.Equal(ProjectionKind.Limit, p.Kind);
        Assert.Equal(1, p.TimeToLimit.TotalHours, 6);
        Assert.Equal(4, p.TimeToReset.TotalHours, 6);
        Assert.Equal(4, p.Ratio, 6);
        Assert.Equal(2, Ember.Core.Render.Gradient.Severity(p.Ratio, 1, 2)); // red
    }

    [Fact]
    public void Lasts_WhenLimitAfterReset()
    {
        // 10% after 2.5 h: rate 4%/h, 90% left → 22.5 h > 2.5 h to reset.
        var p = Projection.Compute(10, Now.AddHours(2.5), Now, Projection.FiveHours);
        Assert.Equal(ProjectionKind.Lasts, p.Kind);
    }

    [Fact]
    public void NoProjection_WhenElapsedUnder10Min()
    {
        var p = Projection.Compute(5, Now.AddHours(5).AddMinutes(-9), Now, Projection.FiveHours);
        Assert.Equal(ProjectionKind.None, p.Kind);
    }

    [Fact]
    public void ZeroUsage_Lasts()
    {
        var p = Projection.Compute(0, Now.AddHours(2), Now, Projection.FiveHours);
        Assert.Equal(ProjectionKind.Lasts, p.Kind);
    }

    [Fact]
    public void FullUsage_LimitNow()
    {
        var p = Projection.Compute(100, Now.AddHours(2), Now, Projection.FiveHours);
        Assert.Equal(ProjectionKind.Limit, p.Kind);
        Assert.Equal(TimeSpan.Zero, p.TimeToLimit);
    }

    [Fact]
    public void Weekly_UsesWindowAverage()
    {
        // 18% after 2 days 22 h (reset in 4d02) → rate 18/70 %/h → limit in ~319 h > 98 h.
        var p = Projection.Compute(18, Now.AddDays(4).AddHours(2), Now, Projection.SevenDays);
        Assert.Equal(ProjectionKind.Lasts, p.Kind);
    }

    [Fact]
    public void OverrideRate_IsUsed()
    {
        // 40% used, reset in 3 h; recent rate 30 %/h → limit in 2 h.
        var p = Projection.Compute(40, Now.AddHours(3), Now, Projection.FiveHours, 30);
        Assert.Equal(ProjectionKind.Limit, p.Kind);
        Assert.Equal(2, p.TimeToLimit.TotalHours, 6);
    }

    [Fact]
    public void NoResetTime_NoProjection()
        => Assert.Equal(ProjectionKind.None, Projection.Compute(50, null, Now, Projection.FiveHours).Kind);
}

public class FiveHourRateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NullBefore30MinOfSamples()
    {
        var r = new FiveHourRate();
        var reset = T0.AddHours(4);
        for (int m = 0; m <= 29; m++) r.Add(T0.AddMinutes(m), m * 0.5, reset);
        Assert.Null(r.Rate(T0.AddMinutes(29)));
    }

    [Fact]
    public void RateOverLast30Min()
    {
        var r = new FiveHourRate();
        var reset = T0.AddHours(4);
        // 0.5 % per minute = 30 %/h
        for (int m = 0; m <= 40; m++) r.Add(T0.AddMinutes(m), m * 0.5, reset);
        Assert.Equal(30, r.Rate(T0.AddMinutes(40))!.Value, 6);
    }

    [Fact]
    public void NewWindowClearsHistory()
    {
        var r = new FiveHourRate();
        for (int m = 0; m <= 40; m++) r.Add(T0.AddMinutes(m), 50 + m * 0.1, T0.AddMinutes(60));
        r.Add(T0.AddMinutes(61), 1, T0.AddHours(6));
        Assert.Null(r.Rate(T0.AddMinutes(61)));
    }

    [Fact]
    public void Tracker_PerModelOnlyWhenHigher()
    {
        var json = """
        {"five_hour":{"utilization":42.0,"resets_at":"2026-10-01T15:10:00+00:00"},
         "seven_day":{"utilization":18.0,"resets_at":"2026-10-05T14:00:00Z"},
         "seven_day_opus":{"utilization":25.0,"resets_at":"2026-10-05T14:00:00Z"},
         "seven_day_sonnet":{"utilization":3.0,"resets_at":"2026-10-05T14:00:00Z"},
         "seven_day_oauth_apps":null}
        """;
        var data = UsageData.Parse(json)!;
        var t = new ClaudeTracker();
        var now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        t.Update(data, now);
        var lines = t.Lines(now);
        Assert.Equal(new[] { "5h", "week", "opus" }, lines.Select(l => l.Label));
        Assert.Equal(TimeSpan.FromMinutes(190), lines[0].ResetIn);
        t.Fail();
        Assert.Empty(t.Lines(now));
    }
}

public class UsageParsingTests
{
    [Fact]
    public void ParsesWindows()
    {
        var d = UsageData.Parse("""{"five_hour":{"utilization":6.0,"resets_at":"2025-11-04T04:59:59.943648+00:00"},"seven_day":{"utilization":35,"resets_at":null},"seven_day_opus":null}""");
        Assert.NotNull(d);
        Assert.Equal(6.0, d!.FiveHour!.Utilization);
        Assert.Equal(new DateTimeOffset(2025, 11, 4, 4, 59, 59, TimeSpan.Zero), d.FiveHour.ResetsAt!.Value.AddTicks(-(d.FiveHour.ResetsAt!.Value.Ticks % TimeSpan.TicksPerSecond)));
        Assert.Equal(35, d.SevenDay!.Utilization);
        Assert.Null(d.SevenDay.ResetsAt);
        Assert.Empty(d.Models);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"error":{"type":"rate_limit_error"}}""")]
    public void InvalidGivesNull(string json) => Assert.Null(UsageData.Parse(json));

    [Fact]
    public void Credentials()
    {
        var t = OAuthToken.Parse("""{"claudeAiOauth":{"accessToken":"sk-ant-oat01-abc","refreshToken":"sk-ant-ort01-x","expiresAt":1760000000000,"scopes":["user:inference"]}}""");
        Assert.NotNull(t);
        Assert.Equal("sk-ant-oat01-abc", t!.AccessToken);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1760000000000), t.ExpiresAt);
        Assert.True(t.IsExpired(DateTimeOffset.FromUnixTimeMilliseconds(1760000000001)));
        Assert.False(t.IsExpired(DateTimeOffset.FromUnixTimeMilliseconds(1759999999999)));
        Assert.Null(OAuthToken.Parse("{}"));
        Assert.Null(OAuthToken.Parse("""{"claudeAiOauth":{"accessToken":""}}"""));
        Assert.Null(OAuthToken.Parse("garbage"));
    }

    [Fact]
    public void Credentials_SubscriptionType()
    {
        Assert.Equal("max", OAuthToken.Parse("""{"claudeAiOauth":{"accessToken":"a","subscriptionType":"max"}}""")!.SubscriptionType);
        Assert.Equal("pro", OAuthToken.Parse("""{"claudeAiOauth":{"accessToken":"a","subscription_type":"pro"}}""")!.SubscriptionType);
        Assert.Null(OAuthToken.Parse("""{"claudeAiOauth":{"accessToken":"a"}}""")!.SubscriptionType);
    }

    [Fact]
    public void Backoff_Plus30sOn429_UpTo5Min_ResetsOnSuccess()
    {
        var b = new PollBackoff(TimeSpan.FromSeconds(15)); // below the minimum: 1 min
        Assert.Equal(TimeSpan.FromMinutes(1), b.Current);
        b.OnRateLimited();
        Assert.Equal(TimeSpan.FromSeconds(90), b.Current);
        b.OnRateLimited();
        Assert.Equal(TimeSpan.FromMinutes(2), b.Current);
        for (int i = 0; i < 20; i++) b.OnRateLimited();
        Assert.Equal(TimeSpan.FromMinutes(5), b.Current);
        b.OnSuccess();
        Assert.Equal(TimeSpan.FromMinutes(1), b.Current);
    }
}
