using System.Globalization;

namespace Ember.Core.Render;

/// <summary>An opaque RGB colour.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    /// <summary>ARGB value for GDI+ (alpha in the high byte).</summary>
    public uint ToArgb(byte alpha = 255) => ((uint)alpha << 24) | ((uint)R << 16) | ((uint)G << 8) | B;

    /// <summary>COLORREF value for GDI (0x00BBGGRR).</summary>
    public uint ToColorRef() => ((uint)B << 16) | ((uint)G << 8) | R;

    public static Rgb FromColorRef(uint c) => new((byte)(c & 0xFF), (byte)((c >> 8) & 0xFF), (byte)((c >> 16) & 0xFF));

    public static Rgb Lerp(Rgb a, Rgb b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new Rgb(
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));
    }

    /// <summary>Colour at position t (0..1) on evenly spaced stops.</summary>
    public static Rgb Stops(IReadOnlyList<Rgb> stops, double t)
    {
        if (stops.Count == 0) return Palette.Bright;
        if (stops.Count == 1 || double.IsNaN(t)) return stops[0];
        t = Math.Clamp(t, 0, 1) * (stops.Count - 1);
        int i = Math.Min((int)Math.Floor(t), stops.Count - 2);
        return Lerp(stops[i], stops[i + 1], t - i);
    }

    /// <summary>Parses "#RRGGBB" or "RRGGBB". Invalid text gives the fallback.</summary>
    public static Rgb Parse(string? hex, Rgb fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        var s = hex.Trim().TrimStart('#');
        if (s.Length == 3) s = string.Concat(s[0], s[0], s[1], s[1], s[2], s[2]);
        if (s.Length != 6 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v)) return fallback;
        return new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public static bool TryParse(string? hex, out Rgb rgb)
    {
        var bad = new Rgb(1, 2, 3);
        rgb = Parse(hex, bad);
        if (rgb != bad) return true;
        return hex?.Trim().TrimStart('#').ToUpperInvariant() is "010203";
    }

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    public override string ToString() => ToHex();
}

/// <summary>Fallback palette (used when no theme is given).</summary>
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
/// 0 = normal colour, 1 = warning colour (at the warning threshold), 2 = critical colour.
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

    public static Rgb Color(double severity, Rgb normal, Rgb warn, Rgb crit)
    {
        if (double.IsNaN(severity) || severity <= 0) return normal;
        if (severity <= 1) return Rgb.Lerp(normal, warn, severity);
        return Rgb.Lerp(warn, crit, severity - 1);
    }

    public static Rgb Color(double severity) => Color(severity, Palette.Bright, Palette.Amber, Palette.Red);

    public static Rgb Color(double value, double warn, double crit) => Color(Severity(value, warn, crit));
}
