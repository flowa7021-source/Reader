using System.Globalization;
using SixLabors.ImageSharp;

namespace Foliant.Rendering.Html;

/// <summary>
/// Minimal CSS colour parsing: <c>#rgb</c> / <c>#rrggbb</c> hex plus a small set of named colours.
/// This is deliberately not the full CSS named-colour list — just the common ones a reader needs.
/// </summary>
internal static class CssColors
{
    private static readonly Dictionary<string, Color> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = Color.Black,
        ["white"] = Color.White,
        ["red"] = Color.Red,
        ["green"] = Color.Green,
        ["blue"] = Color.Blue,
        ["yellow"] = Color.Yellow,
        ["orange"] = Color.Orange,
        ["purple"] = Color.Purple,
        ["gray"] = Color.Gray,
        ["grey"] = Color.Gray,
        ["silver"] = Color.Silver,
        ["maroon"] = Color.Maroon,
        ["navy"] = Color.Navy,
        ["teal"] = Color.Teal,
        ["olive"] = Color.Olive,
        ["lime"] = Color.Lime,
        ["aqua"] = Color.Aqua,
        ["cyan"] = Color.Cyan,
        ["magenta"] = Color.Magenta,
        ["fuchsia"] = Color.Fuchsia,
        ["pink"] = Color.Pink,
        ["brown"] = Color.Brown,
        ["darkgray"] = Color.DarkGray,
        ["darkgrey"] = Color.DarkGray,
        ["lightgray"] = Color.LightGray,
        ["lightgrey"] = Color.LightGray,
    };

    /// <summary>
    /// Attempts to parse a CSS colour token (named or hex). Whitespace is trimmed; the empty string
    /// fails. Hex parsing also accepts the <c>#rrggbbaa</c> form via ImageSharp.
    /// </summary>
    public static bool TryParse(string? value, out Color color)
    {
        color = Color.Black;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string token = value.Trim();

        if (Named.TryGetValue(token, out Color named))
        {
            color = named;
            return true;
        }

        if (token.StartsWith('#'))
        {
            return TryParseHex(token, out color);
        }

        // Functional notation. AngleSharp.Css normalises author colours (even named ones) to
        // rgba(...)/hsla(...), so the cascade path reaches this branch for most real stylesheets.
        if (token.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseRgbFunction(token, out color);
        }

        if (token.StartsWith("hsl", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseHslFunction(token, out color);
        }

        // Allow bare hex digits (rare, but harmless).
        if (IsHexDigits(token) && (token.Length == 3 || token.Length == 6))
        {
            return TryParseHex("#" + token, out color);
        }

        return false;
    }

    /// <summary>Parses <c>rgb(r,g,b)</c> / <c>rgba(r,g,b,a)</c>, accepting comma- or space-separated
    /// components (the modern <c>rgb(r g b / a)</c> form too), each as a 0–255 number or a percentage.</summary>
    private static bool TryParseRgbFunction(string token, out Color color)
    {
        color = Color.Black;
        if (!TryExtractFunctionArgs(token, out string[] args) || args.Length is < 3 or > 4)
        {
            return false;
        }

        if (!TryParseChannel(args[0], out byte r) || !TryParseChannel(args[1], out byte g) || !TryParseChannel(args[2], out byte b))
        {
            return false;
        }

        byte a = 255;
        if (args.Length == 4 && !TryParseAlpha(args[3], out a))
        {
            return false;
        }

        color = Color.FromRgba(r, g, b, a);
        return true;
    }

    /// <summary>Parses <c>hsl(h,s%,l%)</c> / <c>hsla(h,s%,l%,a)</c> into an RGB(A) colour.</summary>
    private static bool TryParseHslFunction(string token, out Color color)
    {
        color = Color.Black;
        if (!TryExtractFunctionArgs(token, out string[] args) || args.Length is < 3 or > 4)
        {
            return false;
        }

        if (!double.TryParse(args[0].Replace("deg", string.Empty, StringComparison.OrdinalIgnoreCase).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double h)
            || !TryParsePercent(args[1], out double s)
            || !TryParsePercent(args[2], out double l))
        {
            return false;
        }

        double alpha = 1.0;
        if (args.Length == 4 && !TryParseAlphaFraction(args[3], out alpha))
        {
            return false;
        }

        (double r, double g, double b) = HslToRgb(((h % 360) + 360) % 360, Math.Clamp(s, 0, 1), Math.Clamp(l, 0, 1));
        color = Color.FromRgba(ToByte(r), ToByte(g), ToByte(b), ToByte(alpha));
        return true;
    }

    private static bool TryExtractFunctionArgs(string token, out string[] args)
    {
        args = [];
        int open = token.IndexOf('(', StringComparison.Ordinal);
        int close = token.LastIndexOf(')');
        if (open < 0 || close <= open)
        {
            return false;
        }

        string inner = token[(open + 1)..close].Replace('/', ',');
        args = inner.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return args.Length > 0;
    }

    private static bool TryParseChannel(string token, out byte channel)
    {
        channel = 0;
        if (token.EndsWith('%'))
        {
            if (!TryParsePercent(token, out double pct))
            {
                return false;
            }

            channel = ToByte(pct);
            return true;
        }

        if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
        {
            channel = ToByte(n / 255.0);
            return true;
        }

        return false;
    }

    private static bool TryParseAlpha(string token, out byte alpha)
    {
        alpha = 255;
        if (TryParseAlphaFraction(token, out double a))
        {
            alpha = ToByte(a);
            return true;
        }

        return false;
    }

    private static bool TryParseAlphaFraction(string token, out double alpha)
    {
        if (token.EndsWith('%'))
        {
            return TryParsePercent(token, out alpha);
        }

        if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out alpha))
        {
            alpha = Math.Clamp(alpha, 0, 1);
            return true;
        }

        return false;
    }

    private static bool TryParsePercent(string token, out double fraction)
    {
        fraction = 0;
        string t = token.Trim();
        if (!t.EndsWith('%'))
        {
            return false;
        }

        if (double.TryParse(t[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double pct))
        {
            fraction = Math.Clamp(pct / 100.0, 0, 1);
            return true;
        }

        return false;
    }

    private static (double R, double G, double B) HslToRgb(double h, double s, double l)
    {
        if (s <= 0)
        {
            return (l, l, l);
        }

        double c = (1 - Math.Abs((2 * l) - 1)) * s;
        double hp = h / 60.0;
        double x = c * (1 - Math.Abs((hp % 2) - 1));
        double m = l - (c / 2);

        (double r, double g, double b) = hp switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return (r + m, g + m, b + m);
    }

    private static byte ToByte(double fraction) => (byte)Math.Clamp(Math.Round(fraction * 255.0), 0, 255);

    private static bool TryParseHex(string token, out Color color)
    {
        // ImageSharp's Color.TryParseHex handles #rgb, #rrggbb and #rrggbbaa.
        if (Color.TryParseHex(token, out color))
        {
            return true;
        }

        color = Color.Black;
        return false;
    }

    private static bool IsHexDigits(string s)
    {
        foreach (char c in s)
        {
            if (!Uri.IsHexDigit(c))
            {
                return false;
            }
        }

        return s.Length > 0;
    }

    /// <summary>Parses a CSS length into CSS pixels, resolving relative units against a parent size.</summary>
    /// <param name="value">The length token (e.g. <c>"14px"</c>, <c>"12pt"</c>, <c>"1.2em"</c>, <c>"120%"</c>).</param>
    /// <param name="parentPx">The parent font size in px, used for <c>em</c>/<c>%</c> resolution.</param>
    /// <param name="result">The resolved length in CSS pixels.</param>
    /// <returns><see langword="true"/> if a numeric length was parsed.</returns>
    public static bool TryParseLengthPx(string? value, double parentPx, out double result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string token = value.Trim();

        (string unit, double factor)[] units =
        [
            ("px", 1.0),
            // 1pt = 1/72in, CSS reference px = 1/96in. NB: layout/paint run at 72 DPI internally
            // (px == pt there), so a pt value becomes 96/72 "px" then rasterizes 1:1 — internally
            // consistent for the MVP; revisit if PR-2b introduces a real device-DPI scale.
            ("pt", 96.0 / 72.0),
            ("rem", parentPx),       // Check before "em" so the longer unit wins; no root tracking in MVP.
            ("em", parentPx),
            ("%", parentPx / 100.0),
        ];

        foreach ((string unit, double factor) in units)
        {
            if (token.EndsWith(unit, StringComparison.OrdinalIgnoreCase))
            {
                string numberPart = token[..^unit.Length].Trim();
                if (double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
                {
                    result = n * factor;
                    return true;
                }

                return false;
            }
        }

        // Unit-less number: treat as pixels.
        if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double bare))
        {
            result = bare;
            return true;
        }

        return false;
    }
}
