using System.Globalization;
using SixLabors.ImageSharp;

namespace Foliant.Rendering.Html;

/// <summary>
/// Computes the <see cref="ComputedStyle"/> of an element by running the CSS cascade: the tag's
/// user-agent defaults (layered over the inherited parent style), then matched author declarations
/// (from <c>&lt;style&gt;</c>/linked CSS, ordered by specificity then source order), then the
/// element's inline <c>style=""</c> attribute — with <c>!important</c> author and inline declarations
/// lifted above all normal ones, in that order. All sizes are in CSS pixels (unscaled). This is an
/// MVP cascade over a small property set, not a full CSS engine.
/// </summary>
internal static class StyleResolver
{
    /// <summary>
    /// Resolves a child element's computed style through the full cascade (UA defaults + author CSS +
    /// inline style).
    /// </summary>
    /// <param name="tag">The lower-cased tag name.</param>
    /// <param name="authorDeclarations">Author declarations matched against this element (any order).
    /// <b>Mutated</b> (sorted in place); the caller must pass a freshly-collected list it owns.</param>
    /// <param name="inlineStyle">The raw <c>style</c> attribute value (may be <see langword="null"/>).</param>
    /// <param name="inherited">The parent's inheritable style (already produced via <see cref="ComputedStyle.InheritTo"/>).</param>
    /// <param name="basePx">The chapter base font size in CSS pixels (root reference for <c>h*</c> scaling).</param>
    public static ComputedStyle Resolve(string tag, List<CssDeclaration> authorDeclarations, string? inlineStyle, ComputedStyle inherited, double basePx)
    {
        ComputedStyle style = ApplyTagDefaults(tag, inherited, basePx);

        // Author declarations: ascending cascade order (non-important by specificity/source order,
        // then all important by specificity/source order). Inline declarations are interleaved at the
        // normal→important boundary so the precedence is: author-normal < inline-normal <
        // author-important < inline-important.
        authorDeclarations.Sort(CssDeclaration.CascadeComparison);

        int i = 0;
        for (; i < authorDeclarations.Count && !authorDeclarations[i].Important; i++)
        {
            style = ApplyDeclaration(style, authorDeclarations[i].Property.ToUpperInvariant(), authorDeclarations[i].Value);
        }

        style = ApplyInlineStyle(style, inlineStyle, important: false);

        for (; i < authorDeclarations.Count; i++)
        {
            style = ApplyDeclaration(style, authorDeclarations[i].Property.ToUpperInvariant(), authorDeclarations[i].Value);
        }

        style = ApplyInlineStyle(style, inlineStyle, important: true);

        return style;
    }

    /// <summary>Fast path for callers with no author stylesheet: UA defaults + inline only, with no
    /// per-element list allocation (the common CSS-less chapter).</summary>
    /// <param name="tag">The lower-cased tag name.</param>
    /// <param name="inlineStyle">The raw <c>style</c> attribute value (may be <see langword="null"/>).</param>
    /// <param name="inherited">The parent's inheritable style.</param>
    /// <param name="basePx">The chapter base font size in CSS pixels.</param>
    public static ComputedStyle Resolve(string tag, string? inlineStyle, ComputedStyle inherited, double basePx)
    {
        ComputedStyle style = ApplyTagDefaults(tag, inherited, basePx);
        style = ApplyInlineStyle(style, inlineStyle, important: false);
        style = ApplyInlineStyle(style, inlineStyle, important: true);
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

    /// <summary>Applies the inline <c>style</c> attribute's declarations of one importance tier.
    /// Called twice per element: once for normal declarations (before author <c>!important</c>) and
    /// once for <c>!important</c> ones (after), so the cascade order is preserved.</summary>
    private static ComputedStyle ApplyInlineStyle(ComputedStyle style, string? inlineStyle, bool important)
    {
        if (string.IsNullOrWhiteSpace(inlineStyle))
        {
            return style;
        }

        foreach (string declaration in inlineStyle.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int colon = declaration.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            string property = declaration[..colon].Trim().ToUpperInvariant();
            string value = declaration[(colon + 1)..].Trim();
            bool valueImportant = StripImportant(ref value);
            if (value.Length == 0 || valueImportant != important)
            {
                continue;
            }

            style = ApplyDeclaration(style, property, value);
        }

        return style;
    }

    /// <summary>Strips a trailing <c>!important</c> (any spacing/case) from a value, reporting whether
    /// it was present. Also fixes the otherwise-broken parse of <c>color:red !important</c> etc.</summary>
    private static bool StripImportant(ref string value)
    {
        int bang = value.LastIndexOf('!');
        if (bang >= 0 && value[(bang + 1)..].Trim().Equals("important", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..bang].TrimEnd();
            return true;
        }

        return false;
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

            case "DISPLAY":
                return ApplyDisplay(style, value);

            case "MARGIN-TOP":
                return CssColors.TryParseLengthPx(value, style.FontSizePx, out double mt)
                    ? style with { MarginTopPx = Math.Max(0, mt) }
                    : style;

            case "MARGIN-BOTTOM":
                return CssColors.TryParseLengthPx(value, style.FontSizePx, out double mb)
                    ? style with { MarginBottomPx = Math.Max(0, mb) }
                    : style;

            case "MARGIN":
                return ApplyMarginShorthand(style, value);

            default:
                return style;
        }
    }

    private static ComputedStyle ApplyDisplay(ComputedStyle style, string value)
    {
        string v = value.Trim();
        if (v.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return style with { Hidden = true };
        }

        if (v.Equals("inline", StringComparison.OrdinalIgnoreCase) || v.Equals("inline-block", StringComparison.OrdinalIgnoreCase))
        {
            return style with { IsBlock = false };
        }

        if (v.Equals("block", StringComparison.OrdinalIgnoreCase) || v.Equals("list-item", StringComparison.OrdinalIgnoreCase))
        {
            return style with { IsBlock = true };
        }

        // flex / grid / table / inline-flex / … : unsupported box model — keep the UA-default flow.
        return style;
    }

    /// <summary>Applies the <c>margin</c> shorthand, taking only the top/bottom edges the layout models
    /// (left/right margins are not part of the reflow box). 1 value = all; 2 = vertical/horizontal;
    /// 3 = top/horizontal/bottom; 4 = top/right/bottom/left. Non-length tokens (e.g. <c>auto</c>) leave
    /// that edge at its UA default.</summary>
    private static ComputedStyle ApplyMarginShorthand(ComputedStyle style, string value)
    {
        string[] parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return style;
        }

        string topToken = parts[0];
        string bottomToken = parts.Length >= 3 ? parts[2] : parts[0];

        if (CssColors.TryParseLengthPx(topToken, style.FontSizePx, out double top))
        {
            style = style with { MarginTopPx = Math.Max(0, top) };
        }

        if (CssColors.TryParseLengthPx(bottomToken, style.FontSizePx, out double bottom))
        {
            style = style with { MarginBottomPx = Math.Max(0, bottom) };
        }

        return style;
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
