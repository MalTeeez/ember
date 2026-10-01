namespace Ember.Core.Stats;

/// <summary>Exponential moving average for each logical core (time constant 3 s).</summary>
public sealed class CoreSmoother
{
    private double[]? _ema;
    private DateTime _last;
    public TimeSpan TimeConstant { get; set; } = TimeSpan.FromSeconds(3);

    public double[] Update(IReadOnlyList<double> raw, DateTime now)
    {
        if (_ema == null || _ema.Length != raw.Count)
        {
            _ema = raw.ToArray();
            _last = now;
            return (double[])_ema.Clone();
        }
        double dt = Math.Max(0, (now - _last).TotalSeconds);
        _last = now;
        double alpha = 1 - Math.Exp(-dt / Math.Max(1e-6, TimeConstant.TotalSeconds));
        for (int i = 0; i < raw.Count; i++)
        {
            double v = raw[i];
            if (double.IsNaN(v)) continue;
            _ema[i] += alpha * (v - _ema[i]);
        }
        return (double[])_ema.Clone();
    }

    public void Reset() => _ema = null;
}

/// <summary>Outlier result for one core.</summary>
public readonly record struct CoreOutlier(int Index, double Load, double Median, double Sigma);

/// <summary>Median, standard deviation and outlier search for core loads.</summary>
public static class OutlierStats
{
    public static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return double.NaN;
        var s = values.OrderBy(v => v).ToArray();
        int n = s.Length;
        return n % 2 == 1 ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) / 2.0;
    }

    /// <summary>Population standard deviation.</summary>
    public static double Sigma(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return double.NaN;
        double mean = values.Average();
        double sum = 0;
        foreach (var v in values) sum += (v - mean) * (v - mean);
        return Math.Sqrt(sum / values.Count);
    }

    /// <summary>
    /// Cores where σ ≥ <paramref name="minSigma"/> and |load − median| &gt; <paramref name="factor"/>·σ.
    /// Median and σ are calculated for each core group (efficiency class) separately.
    /// <paramref name="groups"/> may be null (all cores in one group).
    /// </summary>
    public static List<CoreOutlier> Find(IReadOnlyList<double> loads, IReadOnlyList<int>? groups, double minSigma, double factor)
    {
        var result = new List<CoreOutlier>();
        var byGroup = new Dictionary<int, List<int>>();
        for (int i = 0; i < loads.Count; i++)
        {
            if (double.IsNaN(loads[i])) continue;
            int g = groups != null && i < groups.Count ? groups[i] : 0;
            if (!byGroup.TryGetValue(g, out var list)) byGroup[g] = list = new List<int>();
            list.Add(i);
        }
        foreach (var (_, idx) in byGroup)
        {
            if (idx.Count < 3) continue; // too few cores for a meaningful median/σ
            var vals = idx.Select(i => loads[i]).ToList();
            double med = Median(vals), sigma = Sigma(vals);
            if (sigma < minSigma) continue;
            foreach (var i in idx)
                if (Math.Abs(loads[i] - med) > factor * sigma)
                    result.Add(new CoreOutlier(i, loads[i], med, sigma));
        }
        result.Sort((a, b) => a.Index.CompareTo(b.Index));
        return result;
    }
}

/// <summary>Fixed-size history of samples (oldest first).</summary>
public sealed class History
{
    private readonly Queue<double> _q = new();
    public int Capacity { get; }
    public History(int capacity) => Capacity = capacity;

    public void Add(double v)
    {
        _q.Enqueue(v);
        while (_q.Count > Capacity) _q.Dequeue();
    }

    public IReadOnlyList<double> Values => _q.ToArray();
    public double Max => _q.Count == 0 ? 0 : _q.Max();
    public int Count => _q.Count;
    public void Clear() => _q.Clear();
}

/// <summary>A hot core for the core grid.</summary>
public readonly record struct HotCore(int Index, double Load, double Temp, double ClockMHz, double Score);

/// <summary>
/// Hot-core grid (btop style): 1 column for up to 3 cores, 2 columns for up to 6, 3 columns for up to 9;
/// never more than 3 rows. Cores beyond 9 are summed up in one extra line. The cores to show are
/// chosen by a score of load and temperature: a core that is unusually hot counts even at moderate load.
/// </summary>
public static class CoreGrid
{
    public const int MaxRows = 3, MaxColumns = 3, MaxCells = MaxRows * MaxColumns;

    public static int Columns(int count) => count <= MaxRows ? 1 : count <= 2 * MaxRows ? 2 : MaxColumns;

    /// <summary>
    /// Score = load + 3 × (temperature above the median core temperature + 3 °C) + 30 × temperature severity (0..2).
    /// Without a temperature (NaN) the score is the load.
    /// </summary>
    public static double Score(double load, double temp, double medianTemp, double warn, double crit)
    {
        if (double.IsNaN(temp)) return load;
        double unusual = double.IsNaN(medianTemp) ? 0 : Math.Max(0, temp - medianTemp - 3);
        return load + 3 * unusual + 30 * Render.Gradient.Severity(temp, warn, crit);
    }

    /// <summary>Shown cores (best score first, then sorted by core number) and the hidden rest.</summary>
    public static (List<HotCore> Shown, List<HotCore> Hidden) Select(IEnumerable<HotCore> hot)
    {
        var ordered = hot.OrderByDescending(c => c.Score).ThenBy(c => c.Index).ToList();
        var shown = ordered.Take(MaxCells).OrderBy(c => c.Index).ToList();
        var hidden = ordered.Skip(MaxCells).ToList();
        return (shown, hidden);
    }

    /// <summary>Kubernetes-style total: one fully used core = 100 %.</summary>
    public static double CoreSum(IEnumerable<HotCore> cores) => cores.Sum(c => c.Load);
}

/// <summary>Maps die temperatures to logical cores.</summary>
public static class CoreTempMap
{
    /// <summary>
    /// AMD: physical cores (by their core index) are split evenly over the CCDs in order: with 2 CCDs
    /// and 12 cores, cores 0–5 get CCD1 and 6–11 CCD2. Logical cores of one physical core share it.
    /// Unknown core index (-1) → NaN.
    /// </summary>
    public static double[] ByCcd(IReadOnlyList<int> physicalCoreOfLogical, IReadOnlyList<double> ccdTemps)
    {
        var order = physicalCoreOfLogical.Where(c => c >= 0).Distinct().OrderBy(c => c).ToList();
        var result = new double[physicalCoreOfLogical.Count];
        for (int i = 0; i < result.Length; i++)
        {
            int k = order.IndexOf(physicalCoreOfLogical[i]);
            result[i] = k < 0 || ccdTemps.Count == 0 ? double.NaN : ccdTemps[Math.Min(ccdTemps.Count - 1, k * ccdTemps.Count / order.Count)];
        }
        return result;
    }
}

/// <summary>Sum of a per-second rate over the last minute (packets per minute), from the samples of that minute.</summary>
public sealed class MinuteSum
{
    private readonly Queue<(DateTime Time, double PerSecond)> _q = new();

    /// <summary>Adds a sample and returns the count of the last 60 s (average rate × 60, so gaps do not count as zero).</summary>
    public double Add(DateTime now, double perSecond)
    {
        _q.Enqueue((now, Math.Max(0, perSecond)));
        while (_q.Count > 0 && now - _q.Peek().Time > TimeSpan.FromSeconds(60)) _q.Dequeue();
        return _q.Average(x => x.PerSecond) * 60;
    }
}

/// <summary>
/// Highest value in a sliding time window (default 1 h), in buckets (default 30 s): little memory,
/// good enough to colour a value by how high it is compared to the last hour.
/// </summary>
public sealed class SlidingPeak
{
    private readonly List<(DateTime Start, double Max)> _buckets = new();
    public TimeSpan Window { get; }
    public TimeSpan Bucket { get; }

    public SlidingPeak(TimeSpan? window = null, TimeSpan? bucket = null)
    {
        Window = window ?? TimeSpan.FromHours(1);
        Bucket = bucket ?? TimeSpan.FromSeconds(30);
    }

    public void Add(DateTime now, double value)
    {
        if (double.IsNaN(value)) return;
        int old = 0;
        while (old < _buckets.Count && now - _buckets[old].Start >= Window) old++;
        if (old > 0) _buckets.RemoveRange(0, old);
        if (_buckets.Count > 0 && now >= _buckets[^1].Start && now - _buckets[^1].Start < Bucket)
            _buckets[^1] = (_buckets[^1].Start, Math.Max(_buckets[^1].Max, value));
        else
            _buckets.Add((now, value));
    }

    public double Max => _buckets.Count == 0 ? 0 : _buckets.Max(b => b.Max);

    /// <summary>Value as a fraction of the window peak (0..1).</summary>
    public double Fraction(double value) => Max > 0 ? Math.Clamp(value / Max, 0, 1) : 0;

    public void Clear() => _buckets.Clear();
}
