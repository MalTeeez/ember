using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Ember.Core.Claude;

/// <summary>
/// OpenAI Codex plan limits (same data as /status in the Codex CLI). Not documented; it can change.
/// GET https://chatgpt.com/backend-api/wham/usage
/// Authorization: Bearer &lt;tokens.access_token from %USERPROFILE%\.codex\auth.json&gt;, ChatGPT-Account-Id: &lt;tokens.account_id&gt;
/// Response: { "plan_type": "plus", "rate_limit": { "primary_window": { "used_percent": 41, "limit_window_seconds": 18000,
///   "reset_after_seconds": 60, "reset_at": 1760000000 }, "secondary_window": { … 604800 … } }, … }
/// </summary>
public static class CodexUsage
{
    public const string Url = "https://chatgpt.com/backend-api/wham/usage";

    public sealed record Auth(string AccessToken, string? AccountId, DateTimeOffset? ExpiresAt);

    /// <summary>Reads auth.json: { "tokens": { "access_token", "id_token", "account_id" } }. The token is never refreshed.</summary>
    public static Auth? ParseAuth(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("tokens", out var t) || t.ValueKind != JsonValueKind.Object) return null;
            string? access = Str(t, "access_token");
            if (string.IsNullOrEmpty(access)) return null;
            string? account = Str(t, "account_id");
            var claims = JwtPayload(access);
            if (string.IsNullOrEmpty(account))
                account = AccountFromClaims(claims) ?? AccountFromClaims(JwtPayload(Str(t, "id_token") ?? ""));
            DateTimeOffset? exp = null;
            if (claims is { } c && c.RootElement.TryGetProperty("exp", out var e) && e.TryGetInt64(out long s))
                exp = DateTimeOffset.FromUnixTimeSeconds(s);
            claims?.Dispose();
            return new Auth(access, account, exp);
        }
        catch (JsonException) { return null; }
    }

    private static string? Str(JsonElement o, string name)
        => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Payload of a JWT (no signature check: only used to read the account id and expiry).</summary>
    public static JsonDocument? JwtPayload(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2) return null;
        try
        {
            string b64 = parts[1].Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            return JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
        }
        catch (Exception ex) when (ex is FormatException or JsonException) { return null; }
    }

    private static string? AccountFromClaims(JsonDocument? claims)
    {
        if (claims == null) return null;
        if (claims.RootElement.TryGetProperty("https://api.openai.com/auth", out var auth) && auth.ValueKind == JsonValueKind.Object)
            return Str(auth, "chatgpt_account_id");
        return null;
    }

    /// <summary>Null if not valid. Window labels follow the window length (5h, wk, or "Nd").</summary>
    public static UsageData? Parse(string json, DateTimeOffset now)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("rate_limit", out var rl) || rl.ValueKind != JsonValueKind.Object) return null;
            var primary = Window(rl, "primary_window", now);
            var secondary = Window(rl, "secondary_window", now);
            if (primary == null && secondary == null) return null;
            // If only one window exists and it is long, show it as the long window.
            if (primary != null && secondary == null && primary.Length > TimeSpan.FromDays(1)) (primary, secondary) = (null, primary);
            return new UsageData(primary, secondary, Array.Empty<(string, UsageWindow)>())
            {
                ShortLabel = Label(primary?.Length, "5h"),
                LongLabel = Label(secondary?.Length, "week"),
                Plan = Str(doc.RootElement, "plan_type"),
            };
        }
        catch (JsonException) { return null; }
    }

    private static string Label(TimeSpan? length, string fallback)
    {
        if (length is not { } l) return fallback;
        if (Math.Abs((l - TimeSpan.FromDays(7)).TotalMinutes) < 1) return "week";
        if (l.TotalHours < 24) return Math.Round(l.TotalHours).ToString(CultureInfo.InvariantCulture) + "h";
        return Math.Round(l.TotalDays).ToString(CultureInfo.InvariantCulture) + "d";
    }

    private static UsageWindow? Window(JsonElement rl, string name, DateTimeOffset now)
    {
        if (!rl.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object) return null;
        if (!w.TryGetProperty("used_percent", out var u) || u.ValueKind != JsonValueKind.Number) return null;
        double used = u.GetDouble();
        if (used < 0 || used > 100.5) return null;
        TimeSpan? length = w.TryGetProperty("limit_window_seconds", out var l) && l.TryGetInt64(out long ls) && ls > 0 ? TimeSpan.FromSeconds(ls) : null;
        DateTimeOffset? reset = null;
        if (w.TryGetProperty("reset_at", out var r) && r.ValueKind == JsonValueKind.Number && r.TryGetInt64(out long ra) && ra > 0)
            reset = DateTimeOffset.FromUnixTimeSeconds(ra);
        else if (w.TryGetProperty("reset_after_seconds", out var ras) && ras.ValueKind == JsonValueKind.Number && ras.TryGetInt64(out long after))
            reset = now.AddSeconds(after);
        return new UsageWindow(used, reset, length);
    }
}

/// <summary>
/// GitHub Copilot quotas (same data as the Copilot status in the editors). Not documented; it can change.
/// GET https://api.github.com/copilot_internal/user with Authorization: token &lt;GitHub OAuth token&gt;.
/// Response: { "copilot_plan": "individual", "quota_reset_date": "2026-11-01",
///   "quota_snapshots": { "premium_interactions": { "entitlement": 300, "remaining": 120, "percent_remaining": 40.0, "unlimited": false }, "chat": {…}, "completions": {…} } }
/// </summary>
public static class CopilotUsage
{
    public const string Url = "https://api.github.com/copilot_internal/user";

    /// <summary>
    /// GitHub OAuth token from a Copilot editor plugin file (apps.json / hosts.json in github-copilot):
    /// { "github.com:Iv1…": { "user": "…", "oauth_token": "ghu_…" } } or { "github.com": { "oauth_token": "…" } }.
    /// </summary>
    public static string? TokenFromPluginFile(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (!p.Name.StartsWith("github.com", StringComparison.OrdinalIgnoreCase) || p.Value.ValueKind != JsonValueKind.Object) continue;
                if (p.Value.TryGetProperty("oauth_token", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() is { Length: > 0 } tok)
                    return tok;
            }
            return null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Premium requests as the long window ("premium", monthly); limited chat and completions quotas as extra windows.
    /// Unlimited quotas are left out. Null if nothing is limited or the JSON is not valid.
    /// </summary>
    public static UsageData? Parse(string json, DateTimeOffset now)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            DateTimeOffset? reset = null;
            foreach (var key in new[] { "quota_reset_date_utc", "quota_reset_date", "limited_user_reset_date" })
                if (root.TryGetProperty(key, out var r) && r.ValueKind == JsonValueKind.String &&
                    DateTimeOffset.TryParse(r.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d))
                {
                    reset = d;
                    break;
                }
            TimeSpan? length = reset is { } rs ? rs - rs.AddMonths(-1) : null;
            if (!root.TryGetProperty("quota_snapshots", out var snaps) || snaps.ValueKind != JsonValueKind.Object) return null;
            var premium = Quota(snaps, "premium_interactions", reset, length);
            var extra = new List<(string, UsageWindow)>();
            if (Quota(snaps, "chat", reset, length) is { } chat) extra.Add(("chat", chat));
            if (Quota(snaps, "completions", reset, length) is { } code) extra.Add(("code", code));
            if (premium == null && extra.Count == 0) return null;
            string? plan = null;
            foreach (var key in new[] { "copilot_plan", "access_type_sku" })
                if (root.TryGetProperty(key, out var pv) && pv.ValueKind == JsonValueKind.String && pv.GetString() is { Length: > 0 } ps) { plan = ps; break; }
            return new UsageData(null, premium, extra) { LongLabel = "premium", ModelsAlways = true, Plan = plan };
        }
        catch (JsonException) { return null; }
    }

    private static UsageWindow? Quota(JsonElement snaps, string name, DateTimeOffset? reset, TimeSpan? length)
    {
        if (!snaps.TryGetProperty(name, out var q) || q.ValueKind != JsonValueKind.Object) return null;
        if (q.TryGetProperty("unlimited", out var u) && u.ValueKind == JsonValueKind.True) return null;
        double? used = null;
        if (q.TryGetProperty("percent_remaining", out var pr) && pr.ValueKind == JsonValueKind.Number)
            used = 100 - pr.GetDouble();
        else if (q.TryGetProperty("entitlement", out var en) && en.ValueKind == JsonValueKind.Number && en.GetDouble() > 0 &&
                 q.TryGetProperty("remaining", out var rem) && rem.ValueKind == JsonValueKind.Number)
            used = 100 * (en.GetDouble() - rem.GetDouble()) / en.GetDouble();
        if (used is not { } v) return null;
        return new UsageWindow(Math.Clamp(v, 0, 100), reset, length);
    }
}
