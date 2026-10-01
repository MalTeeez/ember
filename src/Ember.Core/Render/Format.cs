using System.Globalization;

namespace Ember.Core.Render;

/// <summary>Text formats for values. All use the invariant culture.</summary>
public static class Format
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Compact duration: "52m", "3h05m", "1d22h", "4d2h". Negative values give "0m".</summary>
    public static string Duration(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t.TotalHours < 1) return ((int)Math.Floor(t.TotalMinutes)).ToString(Inv) + "m";
        if (t.TotalDays < 1) return ((int)Math.Floor(t.TotalHours)).ToString(Inv) + "h" + t.Minutes.ToString("00", Inv) + "m";
        return ((int)Math.Floor(t.TotalDays)).ToString(Inv) + "d" + t.Hours.ToString(Inv) + "h";
    }

    // ───── 4-character fields: 3 for the value, 1 for the unit letter ─────

    /// <summary>" 23%", "100%".</summary>
    public static string Pct4(double v) => (Math.Round(Math.Clamp(double.IsNaN(v) ? 0 : v, 0, 999)).ToString("0", Inv) + "%").PadLeft(4);

    /// <summary>" 71°".</summary>
    public static string Temp4(double c) => (Math.Round(Math.Clamp(c, -99, 999)).ToString("0", Inv) + "\u00B0").PadLeft(4);

    /// <summary>Value with a unit letter in 4 characters: "2.1M", " 61M", "612M", "1.2G". Base 1000 or 1024.</summary>
    public static string Scaled4(double v, double baseValue, string letters)
    {
        if (double.IsNaN(v) || v < 0) v = 0;
        int i = 0;
        // Move to the next unit when the rounded value would need 4 digits.
        while (i < letters.Length - 1 && Math.Round(v) >= 999.5) { v /= baseValue; i++; }
        string num = v < 9.95 ? v.ToString("0.0", Inv) : Math.Round(v).ToString("0", Inv);
        if (num.Length > 3) num = Math.Round(v) >= 999.5 ? "999" : Math.Round(v).ToString("0", Inv); // above the largest unit: capped
        return (num + letters[i]).PadLeft(4);
    }

    /// <summary>Bits or bytes per second, base 1000: " 61M". Input in bits per second.</summary>
    public static string Rate4(double bitsPerSecond, bool bytes = false)
        => Scaled4((bytes ? bitsPerSecond / 8 : bitsPerSecond) / 1000, 1000, "kMGTP");

    /// <summary>Bytes, base 1024: "7.8G", " 12G", "512M".</summary>
    public static string Bytes4(double bytes) => Scaled4(bytes / 1024, 1024, "KMGTP");

    /// <summary>Clock from MHz: "4.8G", "800M".</summary>
    public static string Clock4(double mhz) => Scaled4(mhz, 1000, "MG");

    /// <summary>Counts: " 980", "1.5k", " 12k".</summary>
    public static string Count4(double n)
    {
        if (double.IsNaN(n) || n < 0) n = 0;
        return Math.Round(n) < 999.5 ? Math.Round(n).ToString("0", Inv).PadLeft(4) : Scaled4(n / 1000, 1000, "kMG");
    }

    /// <summary>Plain number in 4 characters: " 2.4", " 140".</summary>
    public static string Num4(double v)
    {
        if (double.IsNaN(v)) v = 0;
        string s = Math.Abs(v) < 9.95 ? v.ToString("0.0", Inv) : Math.Round(v).ToString("0", Inv);
        return s.Length > 4 ? "999+" : s.PadLeft(4);
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
