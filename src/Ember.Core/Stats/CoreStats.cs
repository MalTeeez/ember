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
