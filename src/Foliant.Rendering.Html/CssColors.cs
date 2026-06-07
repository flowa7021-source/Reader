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

        // Allow bare hex digits (rare, but harmless).
        if (IsHexDigits(token) && (token.Length == 3 || token.Length == 6))
        {
            return TryParseHex("#" + token, out color);
        }

        return false;
    }

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
