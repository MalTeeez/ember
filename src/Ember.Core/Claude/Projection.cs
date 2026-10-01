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

    /// <summary>% per hour over the last 30 min, or null if the samples cover less than 30 min.</summary>
    public double? Rate(DateTimeOffset now)
    {
        if (_samples.Count < 2) return null;
        var target = now - Span;
        if (_samples[0].Time > target) return null; // less than 30 min of samples
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
    /// <summary>Kind from the label when not set ("5h" short, "wk" long, others extra).</summary>
    public UsageKind ResolvedKind => Kind != UsageKind.Auto ? Kind : Label switch { "5h" => UsageKind.Short, "wk" => UsageKind.Long, _ => UsageKind.Extra };
}

/// <summary>Keeps the latest usage data of one provider and creates its lines.</summary>
public class UsageTracker
{
    private readonly FiveHourRate _rate = new();
    public UsageData? Data { get; private set; }
    public DateTimeOffset DataTime { get; private set; }

    public void Update(UsageData data, DateTimeOffset now)
    {
        Data = data;
        DataTime = now;
        if (data.FiveHour is { } f) _rate.Add(now, f.Utilization, f.ResetsAt);
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
            double? rate = Math.Abs((len - Projection.FiveHours).TotalMinutes) < 1 ? _rate.Rate(now) : null;
            lines.Add(new ClaudeLine(d.ShortLabel, f.Utilization, ResetIn(f, now),
                Projection.Compute(f.Utilization, f.ResetsAt, now, len, rate), UsageKind.Short));
        }
        if (d.SevenDay is { } w)
            lines.Add(new ClaudeLine(d.LongLabel, w.Utilization, ResetIn(w, now),
                Projection.Compute(w.Utilization, w.ResetsAt, now, w.Length ?? Projection.SevenDays), UsageKind.Long));
        // Extra windows (Claude per-model weekly limits: only if higher than the general weekly limit).
        foreach (var (model, mw) in d.Models)
            if (d.ModelsAlways || (d.SevenDay is { } wk && mw.Utilization > wk.Utilization))
                lines.Add(new ClaudeLine(d.ModelsAlways ? model : ModelLabel(model), mw.Utilization, ResetIn(mw, now),
                    Projection.Compute(mw.Utilization, mw.ResetsAt, now, mw.Length ?? Projection.SevenDays), UsageKind.Extra));
        return lines;
    }

    private static TimeSpan? ResetIn(UsageWindow w, DateTimeOffset now)
        => w.ResetsAt is { } r ? (r - now < TimeSpan.Zero ? TimeSpan.Zero : r - now) : null;

    public static string ModelLabel(string model) => model.ToLowerInvariant() switch
    {
        "opus" => "op",
        "sonnet" => "son",
        "haiku" => "hai",
        var m => m.Length <= 3 ? m : m[..3],
    };
}

/// <summary>The Claude tracker (same as <see cref="UsageTracker"/>).</summary>
public sealed class ClaudeTracker : UsageTracker { }
