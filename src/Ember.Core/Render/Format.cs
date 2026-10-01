using System.Globalization;

namespace Ember.Core.Render;

/// <summary>Text formats for values. All use the invariant culture.</summary>
public static class Format
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Compact duration: "42m", "3h10", "4d02". Negative values give "0m".</summary>
    public static string Duration(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t.TotalHours < 1) return ((int)Math.Floor(t.TotalMinutes)).ToString(Inv) + "m";
        if (t.TotalDays < 1) return ((int)Math.Floor(t.TotalHours)).ToString(Inv) + "h" + t.Minutes.ToString("00", Inv);
        return ((int)Math.Floor(t.TotalDays)).ToString(Inv) + "d" + t.Hours.ToString("00", Inv);
    }

    /// <summary>A bit-rate unit: name and divisor (bits per second).</summary>
    public readonly record struct RateUnit(string Name, string Short, double Divisor);

    public static readonly RateUnit Kbps = new("kbps", "k", 1e3);
    public static readonly RateUnit Mbps = new("Mbps", "M", 1e6);
    public static readonly RateUnit Gbps = new("Gbps", "G", 1e9);

    /// <summary>Unit chosen so that the peak is at least 1 in that unit (minimum kbps).</summary>
    public static RateUnit UnitFor(double peakBitsPerSecond)
    {
        if (peakBitsPerSecond >= 1e9) return Gbps;
        if (peakBitsPerSecond >= 1e6) return Mbps;
        return Kbps;
    }

    /// <summary>Value in 5 characters, right aligned: "  0.0", " 61.0", "  612".</summary>
    public static string Rate5(double bitsPerSecond, RateUnit unit)
    {
        double v = Math.Max(0, bitsPerSecond / unit.Divisor);
        string s = v < 100 ? v.ToString("0.0", Inv) : Math.Round(v).ToString("0", Inv);
        return s.PadLeft(5);
    }

    /// <summary>Peak as integer in the unit, 3 characters at most ("61", "4", "999").</summary>
    public static string Peak(double bitsPerSecond, RateUnit unit)
    {
        double v = Math.Max(0, bitsPerSecond / unit.Divisor);
        return Math.Min(999, Math.Round(v, MidpointRounding.AwayFromZero)).ToString("0", Inv);
    }

    /// <summary>Compact rate with unit letter: "61.0M", "980k", "1.2G".</summary>
    public static string RateCompact(double bitsPerSecond)
    {
        var u = UnitFor(bitsPerSecond);
        double v = Math.Max(0, bitsPerSecond / u.Divisor);
        string s = v < 10 ? v.ToString("0.0", Inv) : Math.Round(v).ToString("0", Inv);
        return s + u.Short;
    }

    /// <summary>Bytes as "12.3 GB", "512 MB", "40 KB".</summary>
    public static string Bytes(double bytes)
    {
        if (bytes < 0 || double.IsNaN(bytes)) bytes = 0;
        const double K = 1024, M = K * 1024, G = M * 1024, T = G * 1024;
        if (bytes >= T) return (bytes / T).ToString("0.0", Inv) + " TB";
        if (bytes >= G) return (bytes / G).ToString("0.0", Inv) + " GB";
        if (bytes >= M) return (bytes / M).ToString("0", Inv) + " MB";
        return (bytes / K).ToString("0", Inv) + " KB";
    }

    /// <summary>Bytes per second as "45.0 MB/s".</summary>
    public static string BytesPerSecond(double bps)
    {
        if (bps < 0 || double.IsNaN(bps)) bps = 0;
        const double K = 1024, M = K * 1024, G = M * 1024;
        if (bps >= G) return (bps / G).ToString("0.0", Inv) + " GB/s";
        if (bps >= M) return (bps / M).ToString("0.0", Inv) + " MB/s";
        return (bps / K).ToString("0", Inv) + " KB/s";
    }

    public static string Percent(double v, int width = 4)
        => (Math.Round(Math.Clamp(v, 0, 999)).ToString("0", Inv) + "%").PadLeft(width);

    public static string Int(double v) => Math.Round(v).ToString("0", Inv);

    /// <summary>Cut or pad text to exactly <paramref name="width"/> characters. Cut text ends with '…'.</summary>
    public static string Fit(string s, int width)
    {
        if (width <= 0) return "";
        if (s.Length <= width) return s.PadRight(width);
        return s[..(width - 1)] + "…";
    }
}
