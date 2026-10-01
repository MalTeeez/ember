using System.Text.Json;

namespace Ember.Core.Claude;

/// <summary>
/// Claude Code OAuth usage endpoint (same as /usage in Claude Code). Not documented; it can change.
/// GET https://api.anthropic.com/api/oauth/usage
/// Authorization: Bearer &lt;claudeAiOauth.accessToken&gt;, anthropic-beta: oauth-2025-04-20
/// Response: { "five_hour": { "utilization": 42.0, "resets_at": "2026-…Z" }, "seven_day": {…},
///             "seven_day_opus": {…} | null, "seven_day_sonnet": {…} | null, … }
/// utilization is a percentage (0..100).
/// </summary>
public static class UsageApi
{
    public const string Url = "https://api.anthropic.com/api/oauth/usage";
    public const string BetaHeader = "anthropic-beta";
    public const string BetaValue = "oauth-2025-04-20";
}

/// <summary>Token from %USERPROFILE%\.claude\.credentials.json. The token is never refreshed.</summary>
public sealed record OAuthToken(string AccessToken, DateTimeOffset? ExpiresAt)
{
    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } e && e <= now;

    /// <summary>Parses { "claudeAiOauth": { "accessToken": "...", "expiresAt": 1730000000000 } }. Null if not valid.</summary>
    public static OAuthToken? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var o) || o.ValueKind != JsonValueKind.Object) return null;
            if (!o.TryGetProperty("accessToken", out var t) || t.ValueKind != JsonValueKind.String) return null;
            string token = t.GetString() ?? "";
            if (token.Length == 0) return null;
            DateTimeOffset? exp = null;
            if (o.TryGetProperty("expiresAt", out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out long ms))
                exp = DateTimeOffset.FromUnixTimeMilliseconds(ms);
            return new OAuthToken(token, exp);
        }
        catch (JsonException) { return null; }
    }
}

/// <summary>One usage window. Length = window length (null: 5 h for the short, 7 days for the long window).</summary>
public sealed record UsageWindow(double Utilization, DateTimeOffset? ResetsAt, TimeSpan? Length = null);

/// <summary>
/// Parsed usage of one provider: a short window (Claude/Codex 5 h), a long window (weekly, or Copilot's
/// monthly premium requests) and extra windows (per-model limits, Copilot chat/completions).
/// </summary>
public sealed record UsageData(UsageWindow? FiveHour, UsageWindow? SevenDay, IReadOnlyList<(string Model, UsageWindow Window)> Models)
{
    public string ShortLabel { get; init; } = "5h";
    public string LongLabel { get; init; } = "week";
    /// <summary>Show extra windows always (Copilot), not only when higher than the long window (Claude models).</summary>
    public bool ModelsAlways { get; init; }

    /// <summary>Null if the JSON is not valid or has neither window.</summary>
    public static UsageData? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var five = Window(root, "five_hour");
            var week = Window(root, "seven_day");
            if (five == null && week == null) return null;
            var models = new List<(string, UsageWindow)>();
            foreach (var p in root.EnumerateObject())
            {
                if (!p.Name.StartsWith("seven_day_", StringComparison.Ordinal)) continue;
                string model = p.Name["seven_day_".Length..];
                if (model is "oauth_apps" || model.Length == 0) continue; // not a model
                var w = Window(root, p.Name);
                if (w != null) models.Add((model, w));
            }
            return new UsageData(five, week, models);
        }
        catch (JsonException) { return null; }
    }

    private static UsageWindow? Window(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object) return null;
        if (!w.TryGetProperty("utilization", out var u) || u.ValueKind != JsonValueKind.Number) return null;
        DateTimeOffset? reset = null;
        if (w.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(r.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var d))
            reset = d;
        return new UsageWindow(u.GetDouble(), reset);
    }
}

/// <summary>Poll interval with doubling on HTTP 429.</summary>
public sealed class PollBackoff
{
    /// <summary>Default shortest interval (also the shortest retry): 1 min.</summary>
    public static readonly TimeSpan DefaultMin = TimeSpan.FromMinutes(1);
    /// <summary>Shortest interval of this poller.</summary>
    public TimeSpan MinInterval { get; }
    /// <summary>Each HTTP 429 adds this to the interval.</summary>
    public static readonly TimeSpan Step = TimeSpan.FromSeconds(30);

    private TimeSpan _base;
    public TimeSpan Base
    {
        get => _base;
        set
        {
            _base = value < MinInterval ? MinInterval : value;
            if (Current < _base) Current = _base;
        }
    }
    /// <summary>Longest interval after repeated 429 answers: 5 min (or the base, if that is longer).</summary>
    public TimeSpan Max { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan Current { get; private set; }

    public PollBackoff(TimeSpan baseInterval, TimeSpan? minInterval = null)
    {
        MinInterval = minInterval ?? DefaultMin;
        Base = baseInterval;
        Current = Base;
    }

    public void OnSuccess() => Current = Base;

    /// <summary>HTTP 429: 30 s longer each time, up to <see cref="Max"/>.</summary>
    public void OnRateLimited()
    {
        var max = Max > Base ? Max : Base;
        var next = Current + Step;
        Current = next > max ? max : next;
    }

    /// <summary>Other failures keep the current interval.</summary>
    public void OnOtherFailure() { }
}
