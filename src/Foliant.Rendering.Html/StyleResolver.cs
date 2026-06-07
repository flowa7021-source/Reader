using System.Globalization;
using SixLabors.ImageSharp;

namespace Foliant.Rendering.Html;

/// <summary>
/// Computes the <see cref="ComputedStyle"/> of an element from its tag's user-agent defaults layered
/// over the inherited (parent) style, then overlaid with the element's inline <c>style=""</c>
/// attribute. All sizes are in CSS pixels (unscaled). This is an MVP UA stylesheet, not a CSS engine.
/// </summary>
internal static class StyleResolver
{
    /// <summary>
    /// Resolves a child element's computed style.
    /// </summary>
    /// <param name="tag">The lower-cased tag name.</param>
    /// <param name="inlineStyle">The raw <c>style</c> attribute value (may be <see langword="null"/>).</param>
    /// <param name="inherited">The parent's inheritable style (already produced via <see cref="ComputedStyle.InheritTo"/>).</param>
    /// <param name="basePx">The chapter base font size in CSS pixels (root reference for <c>h*</c> scaling).</param>
    public static ComputedStyle Resolve(string tag, string? inlineStyle, ComputedStyle inherited, double basePx)
    {
        ComputedStyle style = ApplyTagDefaults(tag, inherited, basePx);

        if (!string.IsNullOrWhiteSpace(inlineStyle))
        {
            style = ApplyInlineStyle(style, inlineStyle);
        }

        return style;
    }

    private static ComputedStyle ApplyTagDefaults(string tag, ComputedStyle inherited, double basePx)
    {
        // Defaults start from the inherited font/colour/alignment; box props reset per element.
        ComputedStyle s = inherited;

        switch (tag)
        {
            case "h1":
                return s with { IsBlock = true, Bold = true, FontSizePx = basePx * 2.00, MarginTopPx = basePx * 0.67, MarginBottomPx = basePx * 0.67 };
            case "h2":
                return s with { IsBlock = true, Bold = true, FontSizePx = basePx * 1.50, MarginTopPx = basePx * 0.75, MarginBottomPx = basePx * 0.75 };
            case "h3":
                return s with { IsBlock = true, Bold = true, FontSizePx = basePx * 1.30, MarginTopPx = basePx * 0.83, MarginBottomPx = basePx * 0.83 };
            case "h4":
                return s with { IsBlock = true, Bold = true, FontSizePx = basePx * 1.10, MarginTopPx = basePx * 1.00, MarginBottomPx = basePx * 1.00 };
            case "h5":
                return s with { IsBlock = true, Bold = true, FontSizePx = basePx * 0.90, MarginTopPx = basePx * 1.20, MarginBottomPx = basePx * 1.20 };
            case "h6":
                return s with { IsBlock = true, Bold = true, FontSizePx = basePx * 0.80, MarginTopPx = basePx * 1.40, MarginBottomPx = basePx * 1.40 };

            case "p":
                return s with { IsBlock = true, MarginTopPx = basePx * 0.80, MarginBottomPx = basePx * 0.80 };

            case "div":
            case "section":
            case "article":
            case "header":
            case "footer":
            case "main":
            case "figure":
            case "figcaption":
                return s with { IsBlock = true };

            case "blockquote":
                return s with { IsBlock = true, IndentPx = basePx * 2.5, MarginTopPx = basePx * 0.5, MarginBottomPx = basePx * 0.5 };

            case "ul":
                return s with { IsBlock = true, IndentPx = basePx * 2.0, List = ListKind.Unordered, MarginTopPx = basePx * 0.5, MarginBottomPx = basePx * 0.5 };
            case "ol":
                return s with { IsBlock = true, IndentPx = basePx * 2.0, List = ListKind.Ordered, MarginTopPx = basePx * 0.5, MarginBottomPx = basePx * 0.5 };
            case "li":
                return s with { IsBlock = true, MarginTopPx = basePx * 0.15, MarginBottomPx = basePx * 0.15 };

            case "pre":
                return s with { IsBlock = true, Family = GenericFontFamily.Monospace, MarginTopPx = basePx * 0.5, MarginBottomPx = basePx * 0.5 };
            case "code":
            case "kbd":
            case "samp":
            case "tt":
                return s with { Family = GenericFontFamily.Monospace };

            case "b":
            case "strong":
                return s with { Bold = true };
            case "i":
            case "em":
            case "cite":
            case "var":
            case "dfn":
                return s with { Italic = true };

            case "br":
                return s with { ForcesBreak = true };

            // Plain inline elements: inherit unchanged.
            case "span":
            case "a":
            case "small":
            case "sub":
            case "sup":
            case "u":
            case "mark":
            case "abbr":
            case "label":
            default:
                return s;
        }
    }

    private static ComputedStyle ApplyInlineStyle(ComputedStyle style, string inlineStyle)
    {
        foreach (string declaration in inlineStyle.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int colon = declaration.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            string property = declaration[..colon].Trim().ToUpperInvariant();
            string value = declaration[(colon + 1)..].Trim();
            if (value.Length == 0)
            {
                continue;
            }

            style = ApplyDeclaration(style, property, value);
        }

        return style;
    }

    private static ComputedStyle ApplyDeclaration(ComputedStyle style, string property, string value)
    {
        // 'property' is already upper-cased by the caller.
        switch (property)
        {
            case "FONT-WEIGHT":
                return style with { Bold = IsBoldWeight(value) };

            case "FONT-STYLE":
                return style with { Italic = value.Equals("italic", StringComparison.OrdinalIgnoreCase) || value.Equals("oblique", StringComparison.OrdinalIgnoreCase) };

            case "FONT-SIZE":
                return CssColors.TryParseLengthPx(value, style.FontSizePx, out double sizePx) && sizePx > 0
                    ? style with { FontSizePx = sizePx }
                    : style;

            case "COLOR":
                return CssColors.TryParse(value, out Color color)
                    ? style with { Color = color }
                    : style;

            case "TEXT-ALIGN":
                return value.ToUpperInvariant() switch
                {
                    "CENTER" => style with { Align = TextAlign.Center },
                    "RIGHT" => style with { Align = TextAlign.Right },
                    "LEFT" or "START" or "JUSTIFY" => style with { Align = TextAlign.Left },
                    _ => style,
                };

            case "FONT-FAMILY":
                return style with { Family = MapFontFamily(value, style.Family) };

            default:
                return style;
        }
    }

    private static bool IsBoldWeight(string value)
    {
        string v = value.Trim();
        if (v.Equals("bold", StringComparison.OrdinalIgnoreCase) || v.Equals("bolder", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (v.Equals("normal", StringComparison.OrdinalIgnoreCase) || v.Equals("lighter", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Numeric weight: >= 700 is bold.
        return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int weight) && weight >= 700;
    }

    private static GenericFontFamily MapFontFamily(string value, GenericFontFamily fallback)
    {
        if (ContainsAny(value, "monospace", "courier", "mono"))
        {
            return GenericFontFamily.Monospace;
        }

        if (ContainsAny(value, "sans", "arial", "helvetica", "verdana"))
        {
            return GenericFontFamily.SansSerif;
        }

        if (ContainsAny(value, "serif", "times", "georgia"))
        {
            return GenericFontFamily.Serif;
        }

        return fallback;
    }

    private static bool ContainsAny(string haystack, params string[] needles)
    {
        foreach (string needle in needles)
        {
            if (haystack.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
