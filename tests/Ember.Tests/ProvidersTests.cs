using System.Text;
using Ember.Core.Claude;
using Ember.Core.Config;
using Ember.Core.Devices;
using Ember.Core.Render;
using Ember.Core.Status;

namespace Ember.Tests;

public class CodexTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static string Jwt(string payload)
        => "x." + Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".sig";

    [Fact]
    public void Auth_FromTokens()
    {
        var a = CodexUsage.ParseAuth("""{"OPENAI_API_KEY":null,"tokens":{"access_token":"abc.def.ghi","account_id":"acc-1","id_token":"x"},"last_refresh":"2026-01-01"}""");
        Assert.NotNull(a);
        Assert.Equal("abc.def.ghi", a!.AccessToken);
        Assert.Equal("acc-1", a.AccountId);
        Assert.Null(CodexUsage.ParseAuth("""{"tokens":{}}"""));
        Assert.Null(CodexUsage.ParseAuth("nope"));
    }

    [Fact]
    public void Auth_AccountAndExpiryFromJwt()
    {
        string token = Jwt("""{"exp":1760000000,"https://api.openai.com/auth":{"chatgpt_account_id":"acc-jwt"}}""");
        var a = CodexUsage.ParseAuth("{\"tokens\":{\"access_token\":\"" + token + "\"}}")!;
        Assert.Equal("acc-jwt", a.AccountId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1760000000), a.ExpiresAt);
    }

    [Fact]
    public void Parse_Windows()
    {
        var d = CodexUsage.Parse("""
        {"plan_type":"plus","rate_limit":{"allowed":true,"limit_reached":false,
         "primary_window":{"used_percent":41,"limit_window_seconds":18000,"reset_after_seconds":3600,"reset_at":1790000000},
         "secondary_window":{"used_percent":12,"limit_window_seconds":604800,"reset_at":1790500000}}}
        """, Now)!;
        Assert.Equal(41, d.FiveHour!.Utilization);
        Assert.Equal(TimeSpan.FromHours(5), d.FiveHour.Length);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1790000000), d.FiveHour.ResetsAt);
        Assert.Equal(12, d.SevenDay!.Utilization);
        Assert.Equal("5h", d.ShortLabel);
        Assert.Equal("wk", d.LongLabel);
    }

    [Fact]
    public void Parse_ResetAfterAndOnlyWeekly()
    {
        var d = CodexUsage.Parse("""{"rate_limit":{"primary_window":{"used_percent":5,"limit_window_seconds":604800,"reset_after_seconds":60}}}""", Now)!;
        Assert.Null(d.FiveHour);
        Assert.Equal(Now.AddSeconds(60), d.SevenDay!.ResetsAt);
        Assert.Null(CodexUsage.Parse("""{"rate_limit":{"primary_window":{"used_percent":500}}}""", Now));
        Assert.Null(CodexUsage.Parse("{}", Now));
    }
}

public class CopilotTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TokenFromPluginFiles()
    {
        Assert.Equal("ghu_a", CopilotUsage.TokenFromPluginFile("""{"github.com:Iv1.b507a08c87ecfe98":{"user":"me","oauth_token":"ghu_a","githubAppId":"Iv1"}}"""));
        Assert.Equal("gho_b", CopilotUsage.TokenFromPluginFile("""{"github.com":{"user":"me","oauth_token":"gho_b"}}"""));
        Assert.Null(CopilotUsage.TokenFromPluginFile("""{"ghe.example.com":{"oauth_token":"x"}}"""));
        Assert.Null(CopilotUsage.TokenFromPluginFile("bad"));
    }

    [Fact]
    public void Parse_PremiumMonthly()
    {
        var d = CopilotUsage.Parse("""
        {"copilot_plan":"individual","quota_reset_date":"2026-11-01",
         "quota_snapshots":{
           "premium_interactions":{"entitlement":300,"remaining":120,"percent_remaining":40.0,"unlimited":false},
           "chat":{"entitlement":0,"remaining":0,"percent_remaining":100.0,"unlimited":true},
           "completions":{"unlimited":true}}}
        """, Now)!;
        Assert.Equal(60, d.SevenDay!.Utilization, 6);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), d.SevenDay.ResetsAt);
        Assert.Equal(TimeSpan.FromDays(31), d.SevenDay.Length);
        Assert.Equal("prem", d.LongLabel);
        Assert.Empty(d.Models);
    }

    [Fact]
    public void Parse_FreePlanQuotas_FromEntitlement()
    {
        var d = CopilotUsage.Parse("""
        {"quota_reset_date":"2026-11-01","quota_snapshots":{
           "chat":{"entitlement":50,"remaining":10,"unlimited":false},
           "completions":{"entitlement":2000,"remaining":1500,"unlimited":false}}}
        """, Now)!;
        Assert.Null(d.SevenDay);
        Assert.Equal(new[] { "chat", "code" }, d.Models.Select(m => m.Model));
        Assert.Equal(80, d.Models[0].Window.Utilization, 6);
        var t = new UsageTracker();
        t.Update(d, Now);
        var lines = t.Lines(Now);
        Assert.Equal(new[] { "chat", "code" }, lines.Select(l => l.Label));
        Assert.All(lines, l => Assert.Equal(UsageKind.Extra, l.Kind));
    }

    [Fact]
    public void Parse_AllUnlimited_IsNull()
        => Assert.Null(CopilotUsage.Parse("""{"quota_snapshots":{"premium_interactions":{"unlimited":true}}}""", Now));
}

public class RazerTests
{
    [Fact]
    public void Request_Layout_And_Crc()
    {
        var r = RazerProtocol.Request(0x1F, 0x07, 0x80, 0x02);
        Assert.Equal(90, r.Length);
        Assert.Equal(0x00, r[0]);
        Assert.Equal(0x1F, r[1]);
        Assert.Equal(0x02, r[5]);
        Assert.Equal(0x07, r[6]);
        Assert.Equal(0x80, r[7]);
        Assert.Equal((byte)(0x02 ^ 0x07 ^ 0x80), r[88]); // XOR of bytes 2..87
    }

    private static byte[] Response(byte status, byte cmd, byte level)
    {
        var r = RazerProtocol.Request(0x1F, 0x07, cmd, 0x02);
        r[0] = status;
        r[9] = level;
        r[88] = RazerProtocol.Crc(r);
        return r;
    }

    [Fact]
    public void Battery_Parse()
    {
        Assert.Equal(67.1, RazerProtocol.BatteryPercent(Response(0x02, 0x80, 0xAB))!.Value, 1);
        Assert.Equal(100, RazerProtocol.BatteryPercent(Response(0x02, 0x80, 0xFF)));
        Assert.Null(RazerProtocol.BatteryPercent(Response(0x04, 0x80, 0xAB)));   // timeout (mouse off)
        Assert.Null(RazerProtocol.BatteryPercent(Response(0x02, 0x84, 0xAB)));   // other command
        var bad = Response(0x02, 0x80, 0xAB);
        bad[88] ^= 1;
        Assert.Null(RazerProtocol.BatteryPercent(bad));
        Assert.Equal(true, RazerProtocol.Charging(Response(0x02, 0x84, 1)));
        Assert.Equal(false, RazerProtocol.Charging(Response(0x02, 0x84, 0)));
    }

    [Theory]
    [InlineData(0x00C5, null, true)]
    [InlineData(0x0099, "Razer HyperSpeed Wireless Dongle", true)]
    [InlineData(0x0099, "Razer BlackWidow V3", false)]
    public void IsReceiver(int pid, string? name, bool expected) => Assert.Equal(expected, RazerProtocol.IsReceiver((ushort)pid, name));

    [Fact]
    public void Names_ShortAndMerged()
    {
        Assert.Equal("DeathAdder V3 HyperSpeed", DeviceNames.Short("Razer DeathAdder V3 HyperSpeed"));
        var merged = DeviceNames.Merge(new[]
        {
            new DeviceBattery("WH-1000XM5 Hands-Free AG", 70, null, "bluetooth"),
            new DeviceBattery("WH-1000XM5", 60, null, "bluetooth"),
            new DeviceBattery("Razer DeathAdder V3 HyperSpeed", 40, false, "razer"),
        });
        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, b => b.Name == "WH-1000XM5" && b.Percent == 60);
    }
}

public class ProviderComposeTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 12, 0, 0);

    private static EmberSettings Cfg()
    {
        var s = new EmberSettings();
        s.Entries.First(e => e.Id == "toolbar").Enabled = false;
        s.Layout.HoldSeconds = 0;
        return s;
    }

    [Fact]
    public void CodexAndCopilot_Sections()
    {
        var s = new Snapshot
        {
            Time = T0,
            Codex = new List<ClaudeLine>
            {
                new("5h", 20, TimeSpan.FromHours(2), new ProjectionResult(ProjectionKind.Lasts, TimeSpan.MaxValue, TimeSpan.FromHours(2)), UsageKind.Short),
            },
            Copilot = new List<ClaudeLine>
            {
                new("prem", 57, TimeSpan.FromDays(12), new ProjectionResult(ProjectionKind.Lasts, TimeSpan.MaxValue, TimeSpan.FromDays(12)), UsageKind.Long),
            },
        };
        var lines = new StatusComposer().Compose(s, Cfg());
        int cx = lines.FindIndex(l => l.Text.StartsWith("── codex"));
        int cp = lines.FindIndex(l => l.Text.StartsWith("── copilot"));
        Assert.True(cx >= 0 && cp > cx);
        Assert.StartsWith("5h   ", lines[cx + 1].Text);
        Assert.StartsWith("prem ", lines[cp + 1].Text);
        Assert.EndsWith(" 57%", lines[cp + 1].Text);
        Assert.EndsWith(" 12d0h", lines[cp + 2].Text);
    }

    [Fact]
    public void Batteries_Line_And_LowOnly()
    {
        var cfg = Cfg();
        var s = new Snapshot
        {
            Time = T0,
            Batteries = new List<DeviceBattery> { new("DeathAdder V3 HyperSpeed", 12, true, "razer"), new("Buds", 80, null, "bluetooth") },
        };
        var lines = new StatusComposer().Compose(s, cfg);
        int h = lines.FindIndex(l => l.Text.StartsWith("── devices"));
        Assert.True(h >= 0);
        Assert.StartsWith("DeathAdder V3\u2026 + ", lines[h + 1].Text);
        Assert.EndsWith(" 12%", lines[h + 1].Text);
        Assert.Contains(lines[h + 1].Runs, r => r.Color == Rgb.Parse(cfg.Theme.Crit, Palette.Red));
        Assert.All(lines, l => Assert.Equal(Layout.Width, l.Length));

        cfg.Fields.BatteryAlways = false;
        cfg.Intervals.ShowDelaySeconds = 0;
        var low = new StatusComposer().Compose(s, cfg);
        Assert.Contains(low, l => l.Text.StartsWith("DeathAdder"));
        Assert.DoesNotContain(low, l => l.Text.StartsWith("Buds"));
    }

    [Fact]
    public void OldSettings_GetNewSectionsOn()
    {
        var old = new List<LayoutEntry> { LayoutEntry.Item("cpu.load"), LayoutEntry.Item("claude.5h") };
        var n = LayoutPreset.Normalize(old, addMissingAsDefault: true);
        Assert.Contains(n, e => e.Kind == "header" && e.Text == "codex");
        Assert.Contains(n, e => e.Id == "codex.5h" && e.Enabled);
        Assert.Contains(n, e => e.Id == "devices.battery" && e.Enabled);
        Assert.Contains(n, e => e.Id == "gpu.vram" && !e.Enabled); // off in the default layout
        // Import keeps missing items off.
        Assert.Contains(LayoutPreset.Normalize(old), e => e.Id == "codex.5h" && !e.Enabled);
    }
}
