namespace Ember.Core.Render;

/// <summary>An opaque RGB colour.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    /// <summary>ARGB value for GDI+ (alpha in the high byte).</summary>
    public uint ToArgb(byte alpha = 255) => ((uint)alpha << 24) | ((uint)R << 16) | ((uint)G << 8) | B;

    /// <summary>COLORREF value for GDI (0x00BBGGRR).</summary>
    public uint ToColorRef() => ((uint)B << 16) | ((uint)G << 8) | R;

    public static Rgb Lerp(Rgb a, Rgb b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new Rgb(
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));
    }

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>The fixed widget palette (monochrome plus amber and red).</summary>
public static class Palette
{
    public static readonly Rgb Bright = new(0xE8, 0xE8, 0xE8);
    public static readonly Rgb Dim = new(0x80, 0x80, 0x80);
    public static readonly Rgb Amber = new(0xFF, 0xB0, 0x00);
    public static readonly Rgb Red = new(0xFF, 0x45, 0x3A);
    public static readonly Rgb Background = new(0x16, 0x16, 0x16);
    public static readonly Rgb CodeBackground = new(0x2A, 0x2A, 0x2A);
}

/// <summary>
/// Threshold gradient. Severity is a number from 0 to 2:
/// 0 = normal colour, 1 = amber (at the warning threshold), 2 = red (at the critical threshold).
/// Between the points the colour moves linearly. The gradient starts one
/// (critical − warning) span before the warning threshold.
/// If critical &lt; warning, lower values are worse (for example free RAM).
/// </summary>
public static class Gradient
{
    public static double Severity(double value, double warn, double crit)
    {
        if (double.IsNaN(value)) return 0;
        if (crit < warn)
        {
            value = -value; warn = -warn; crit = -crit;
        }
        double span = crit - warn;
        if (span <= 0)
            return value >= warn ? 2 : 0;
        double start = warn - span;
        if (value <= start) return 0;
        if (value < warn) return (value - start) / span;
        if (value < crit) return 1 + (value - warn) / span;
        return 2;
    }

    public static Rgb Color(double severity)
    {
        if (double.IsNaN(severity) || severity <= 0) return Palette.Bright;
        if (severity <= 1) return Rgb.Lerp(Palette.Bright, Palette.Amber, severity);
        return Rgb.Lerp(Palette.Amber, Palette.Red, severity - 1);
    }

    public static Rgb Color(double value, double warn, double crit) => Color(Severity(value, warn, crit));
}
