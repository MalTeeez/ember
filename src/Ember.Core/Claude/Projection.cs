namespace Ember.Core.Claude;

public enum ProjectionKind
{
    /// <summary>Elapsed &lt; 10 min (or no reset time): show "—".</summary>
    None,
    /// <summary>The limit comes after the reset: "lasts" in grey.</summary>
    Lasts,
    /// <summary>The limit comes before the reset: time to limit with gradient colour.</summary>
    Limit,
}

public readonly record struct ProjectionResult(ProjectionKind Kind, TimeSpan TimeToLimit, TimeSpan TimeToReset)
{
    /// <summary>Time to reset ÷ time to limit (≥ 1 when the limit comes first). Used for the gradient.</summary>
    public double Ratio => TimeToLimit.TotalSeconds <= 0 ? double.PositiveInfinity : TimeToReset.TotalSeconds / TimeToLimit.TotalSeconds;
}

/// <summary>
/// elapsed = W − (t_reset − t_now); rate = used / elapsed; t_limit = (100 − used) / rate.
/// </summary>
public static class Projection
{
    public static readonly TimeSpan FiveHours = TimeSpan.FromHours(5);
    public static readonly TimeSpan SevenDays = TimeSpan.FromDays(7);
    public static readonly TimeSpan MinElapsed = TimeSpan.FromMinutes(10);

    /// <param name="usedPercent">Used % now.</param>
    /// <param name="window">W.</param>
    /// <param name="ratePerHour">Optional rate (% per hour). Null = window average.</param>
    public static ProjectionResult Compute(double usedPercent, DateTimeOffset? resetsAt, DateTimeOffset now, TimeSpan window, double? ratePerHour = null)
    {
        if (resetsAt is not { } reset) return new(ProjectionKind.None, TimeSpan.Zero, TimeSpan.Zero);
        var toReset = reset - now;
        if (toReset < TimeSpan.Zero) toReset = TimeSpan.Zero;
        var elapsed = window - toReset;
        if (elapsed < MinElapsed) return new(ProjectionKind.None, TimeSpan.Zero, toReset);
        if (usedPercent >= 100) return new(ProjectionKind.Limit, TimeSpan.Zero, toReset);

        double rate = ratePerHour ?? usedPercent / elapsed.TotalHours;
        if (rate <= 0 || double.IsNaN(rate)) return new(ProjectionKind.Lasts, TimeSpan.MaxValue, toReset);
        double hours = (100 - usedPercent) / rate;
        if (hours > 24 * 365) return new(ProjectionKind.Lasts, TimeSpan.MaxValue, toReset);
        var toLimit = TimeSpan.FromHours(hours);
        return toLimit >= toReset
            ? new(ProjectionKind.Lasts, toLimit, toReset)
            : new(ProjectionKind.Limit, toLimit, toReset);
    }
}

/// <summary>
/// The widget's own 5h samples. The 5h rate uses the last 30 min of samples;
/// before 30 min of samples exist, the caller uses the window average (null rate).
/// </summary>
public sealed class FiveHourRate
{
    private readonly List<(DateTimeOffset Time, double Used)> _samples = new();
    private DateTimeOffset? _reset;
    public TimeSpan Span { get; set; } = TimeSpan.FromMinutes(30);
    /// <summary>The samples must cover at least this time for a rate (default: the whole span).</summary>
    public TimeSpan? MinSpan { get; set; }
    public IReadOnlyList<(DateTimeOffset Time, double Used)> Samples => _samples;
    public DateTimeOffset? Reset => _reset;

    public void Add(DateTimeOffset time, double used, DateTimeOffset? resetsAt)
    {
        // A new window (reset moved by more than 5 min) or a drop in usage starts a new history.
        bool newWindow = resetsAt is { } r && _reset is { } old && Math.Abs((r - old).TotalMinutes) > 5;
        bool dropped = _samples.Count > 0 && used < _samples[^1].Used - 0.5;
        if (newWindow || dropped) _samples.Clear();
        if (resetsAt != null) _reset = resetsAt;
        _samples.Add((time, used));
        // Keep a little more than the span.
        var cutoff = time - Span - TimeSpan.FromMinutes(5);
        _samples.RemoveAll(s => s.Time < cutoff);
    }

    /// <summary>% per hour over the span (or the covered part of it, if at least <see cref="MinSpan"/>), else null.</summary>
    public double? Rate(DateTimeOffset now)
    {
        if (_samples.Count < 2) return null;
        var target = now - Span;
        if (_samples[0].Time > now - (MinSpan ?? Span)) return null; // too little history
        // Latest sample at or before the target time.
        var start = _samples[0];
        foreach (var s in _samples)
        {
            if (s.Time <= target) start = s;
            else break;
        }
        var end = _samples[^1];
        double hours = (end.Time - start.Time).TotalHours;
        if (hours <= 0) return null;
        return Math.Max(0, (end.Used - start.Used) / hours);
    }

    /// <summary>Time until <see cref="Rate"/> has enough history (zero when ready).</summary>
    public TimeSpan ReadyIn(DateTimeOffset now)
    {
        var need = MinSpan ?? Span;
        if (_samples.Count == 0) return need;
        var left = _samples[0].Time + need - now;
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    public void Clear()
    {
        _samples.Clear();
        _reset = null;
    }
}

public enum UsageKind { Auto, Short, Long, Extra }

/// <summary>One usage limit for the widget (Claude, Codex or Copilot).</summary>
public sealed record ClaudeLine(string Label, double Used, TimeSpan? ResetIn, ProjectionResult Projection, UsageKind Kind = UsageKind.Auto)
{
    /// <summary>The projection uses the recent pace (own samples), not the window average.</summary>
    public bool RecentPace { get; init; }
    /// <summary>The pace the projection uses (% per hour), when known.</summary>
    public double? PacePerHour { get; init; }
    /// <summary>Time until the recent pace is used (own samples still too short); null when it is never used.</summary>
    public TimeSpan? RecentPaceIn { get; init; }
    /// <summary>Kind from the label when not set ("5h" short, "week" long, others extra).</summary>
    public UsageKind ResolvedKind => Kind != UsageKind.Auto ? Kind : Label switch { "5h" => UsageKind.Short, "week" or "wk" => UsageKind.Long, _ => UsageKind.Extra };
}

/// <summary>Keeps the latest usage data of one provider and creates its lines.</summary>
public class UsageTracker
{
    private readonly FiveHourRate _rate = new();
    /// <summary>
    /// Pace of the long window: the last 24 h of own samples (at least 12 h needed, kept across restarts),
    /// so a heavy start of the week does not make the projection too early; else the window average.
    /// </summary>
    private readonly FiveHourRate _longRate = NewLongRate();
    /// <summary>Recent pace of each extra (per-model) window; in memory only.</summary>
    private readonly Dictionary<string, FiveHourRate> _modelRates = new();

    private static FiveHourRate NewLongRate() => new() { Span = TimeSpan.FromHours(24), MinSpan = TimeSpan.FromHours(6) };
    public UsageData? Data { get; private set; }
    public DateTimeOffset DataTime { get; private set; }

    public void Update(UsageData data, DateTimeOffset now)
    {
        Data = data;
        DataTime = now;
        if (data.FiveHour is { } f) _rate.Add(now, f.Utilization, f.ResetsAt);
        if (data.SevenDay is { } w) _longRate.Add(now, w.Utilization, w.ResetsAt);
        foreach (var (model, mw) in data.Models)
        {
            if (!_modelRates.TryGetValue(model, out var r)) _modelRates[model] = r = NewLongRate();
            r.Add(now, mw.Utilization, mw.ResetsAt);
        }
    }

    /// <summary>Long-window samples as text ("unix-seconds used reset-unix-seconds" per line), to keep them across restarts.</summary>
    public string ExportHistory()
    {
        var reset = _longRate.Reset?.ToUnixTimeSeconds() ?? 0;
        return string.Join("\n", _longRate.Samples.Select(x =>
            $"{x.Time.ToUnixTimeSeconds()} {x.Used.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} {reset}"));
    }

    public void ImportHistory(string text, DateTimeOffset now)
    {
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.Trim().Split(' ');
            if (p.Length < 3 || !long.TryParse(p[0], out long t) || !double.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double used)
                || !long.TryParse(p[2], out long r)) continue;
            var time = DateTimeOffset.FromUnixTimeSeconds(t);
            if (time > now || now - time > TimeSpan.FromHours(25)) continue;
            _longRate.Add(time, used, r > 0 ? DateTimeOffset.FromUnixTimeSeconds(r) : null);
        }
    }

    /// <summary>Hide the lines (token expired, request failed).</summary>
    public void Fail() => Data = null;

    public IReadOnlyList<ClaudeLine> Lines(DateTimeOffset now)
    {
        var lines = new List<ClaudeLine>();
        if (Data is not { } d) return lines;
        if (d.FiveHour is { } f)
        {
            // The short window follows the current pace (last 30 min of own samples) when the window is about 5 h.
            var len = f.Length ?? Projection.FiveHours;
            bool fiveHour = Math.Abs((len - Projection.FiveHours).TotalMinutes) < 1;
            lines.Add(Line(d.ShortLabel, f, len, fiveHour ? _rate : null, UsageKind.Short, now));
        }
        if (d.SevenDay is { } w)
            lines.Add(Line(d.LongLabel, w, w.Length ?? Projection.SevenDays, _longRate, UsageKind.Long, now));
        // Extra windows (Claude per-model weekly limits: only if higher than the general weekly limit).
        foreach (var (model, mw) in d.Models)
            if (d.ModelsAlways || (d.SevenDay is { } wk && mw.Utilization > wk.Utilization))
                lines.Add(Line(d.ModelsAlways ? model : ModelLabel(model), mw, mw.Length ?? Projection.SevenDays,
                    _modelRates.GetValueOrDefault(model), UsageKind.Extra, now));
        return lines;
    }

    private static ClaudeLine Line(string label, UsageWindow w, TimeSpan len, FiveHourRate? recent, UsageKind kind, DateTimeOffset now)
    {
        double? rate = recent?.Rate(now);
        double? pace = rate;
        if (pace == null && w.ResetsAt is { } r)
        {
            var elapsed = len - (r - now);
            if (elapsed >= Projection.MinElapsed) pace = w.Utilization / elapsed.TotalHours;
        }
        return new ClaudeLine(label, w.Utilization, ResetIn(w, now), Projection.Compute(w.Utilization, w.ResetsAt, now, len, rate), kind)
        {
            RecentPace = rate != null,
            PacePerHour = pace,
            RecentPaceIn = recent == null || rate != null ? null : recent.ReadyIn(now),
        };
    }

    private static TimeSpan? ResetIn(UsageWindow w, DateTimeOffset now)
        => w.ResetsAt is { } r ? (r - now < TimeSpan.Zero ? TimeSpan.Zero : r - now) : null;

    /// <summary>Model name as label: full when short ("opus", "sonnet"), else cut to 8 characters.</summary>
    public static string ModelLabel(string model)
    {
        string m = model.Trim().ToLowerInvariant();
        return m.Length <= 8 ? m : m[..8];
    }
}

/// <summary>The Claude tracker (same as <see cref="UsageTracker"/>).</summary>
public sealed class ClaudeTracker : UsageTracker { }
