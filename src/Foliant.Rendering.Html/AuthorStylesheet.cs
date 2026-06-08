using System.Diagnostics.CodeAnalysis;
using AngleSharp.Css;
using AngleSharp.Css.Dom;
using AngleSharp.Css.Parser;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;

namespace Foliant.Rendering.Html;

/// <summary>
/// One author CSS declaration matched against an element, carrying the cascade metadata needed to
/// order it: its selector <see cref="Specificity"/>, the source <see cref="Order"/> (rule index, for
/// the specificity tie-break), and whether it carried <c>!important</c>. Property names are raw
/// (lower-case CSS, e.g. <c>font-size</c>); <see cref="StyleResolver"/> upper-cases before mapping.
/// </summary>
/// <param name="Property">The CSS property name (e.g. <c>color</c>).</param>
/// <param name="Value">The CSS value text (e.g. <c>#333</c>, <c>1.2em</c>).</param>
/// <param name="Important">Whether the declaration carried <c>!important</c>.</param>
/// <param name="Specificity">The matching selector's specificity (AngleSharp-computed).</param>
/// <param name="Order">The rule's source order (lower appears earlier); breaks specificity ties.</param>
internal readonly record struct CssDeclaration(
    string Property,
    string Value,
    bool Important,
    Priority Specificity,
    int Order)
{
    /// <summary>Cascade comparison (ascending = applied first = lowest precedence): non-important
    /// before important, then by ascending specificity, then by ascending source order. Folding the
    /// sorted list front-to-back therefore lets the highest-precedence declaration win (it is applied
    /// last). Used by <see cref="StyleResolver"/>.</summary>
    public static int CascadeComparison(CssDeclaration a, CssDeclaration b)
    {
        int c = a.Important.CompareTo(b.Important);
        if (c != 0)
        {
            return c;
        }

        c = a.Specificity.CompareTo(b.Specificity);
        return c != 0 ? c : a.Order.CompareTo(b.Order);
    }
}

/// <summary>
/// A parsed set of author CSS style rules (a chapter's <c>&lt;style&gt;</c> blocks plus any linked
/// stylesheets) able to produce, for any element, the declarations that match it — the
/// selector-matching + specificity half of the CSS cascade. The other half (user-agent defaults,
/// inline <c>style</c>, and the property → <see cref="ComputedStyle"/> mapping) lives in
/// <see cref="StyleResolver"/>.
///
/// <para><b>MVP scope.</b> Only top-level <see cref="ICssStyleRule"/>s are honoured (ordinary selector
/// rules); at-rules — <c>@media</c>, <c>@font-face</c>, <c>@import</c>, <c>@keyframes</c>, … — are
/// ignored. Selectors are matched with AngleSharp's own engine, so the selector grammar it supports
/// works (type/class/id/attribute/combinators/pseudo-classes); a pathological or unsupported selector
/// is skipped defensively rather than throwing into layout. The instance is immutable and safe for
/// concurrent use.</para>
/// </summary>
internal sealed class AuthorStylesheet
{
    /// <summary>The shared empty stylesheet — no rules, matches nothing. Lets the layout walk take a
    /// fast path (no per-element allocation) for the common CSS-less chapter.</summary>
    public static AuthorStylesheet Empty { get; } = new([]);

    private readonly IReadOnlyList<ICssStyleRule> _rules;

    private AuthorStylesheet(IReadOnlyList<ICssStyleRule> rules) => _rules = rules;

    /// <summary>True when there are no style rules (so element matching can be skipped entirely).</summary>
    public bool IsEmpty => _rules.Count == 0;

    /// <summary>Parses <paramref name="sources"/> (already in document/cascade order) into one
    /// stylesheet. Each source is parsed independently so one malformed sheet cannot poison the rest;
    /// a source that fails to parse is logged and skipped. Returns <see cref="Empty"/> when no usable
    /// rule is found.</summary>
    /// <param name="sources">CSS text fragments in document order (<c>&lt;style&gt;</c> bodies and
    /// resolved <c>&lt;link&gt;</c> contents).</param>
    /// <param name="log">Logger for non-fatal parse diagnostics.</param>
    /// <returns>The parsed stylesheet, or <see cref="Empty"/>.</returns>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Robustness contract: malformed author CSS must degrade (be skipped), never throw into the layout walk. The CSS parser does not document a closed exception set.")]
    public static AuthorStylesheet Parse(IReadOnlyList<string> sources, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(log);
        if (sources.Count == 0)
        {
            return Empty;
        }

        var parser = new CssParser();
        var rules = new List<ICssStyleRule>();
        foreach (string source in sources)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            try
            {
                ICssStyleSheet sheet = parser.ParseStyleSheet(source);
                foreach (ICssRule rule in sheet.Rules)
                {
                    if (rule is ICssStyleRule styleRule)
                    {
                        rules.Add(styleRule);
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Failed to parse an author CSS source; skipping it.");
            }
        }

        return rules.Count == 0 ? Empty : new AuthorStylesheet(rules);
    }

    /// <summary>Appends to <paramref name="into"/> every declaration of every rule whose selector
    /// matches <paramref name="element"/>, each tagged with its specificity and source order. The
    /// caller adds the inline declarations, sorts, and folds the whole set onto the computed style.</summary>
    /// <param name="element">The element to match rules against.</param>
    /// <param name="into">The accumulator the matched declarations are appended to.</param>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Robustness contract: a pathological/unsupported selector must be skipped, not throw into the layout walk. AngleSharp's matcher does not document a closed exception set.")]
    public void CollectMatching(IElement element, List<CssDeclaration> into)
    {
        for (int order = 0; order < _rules.Count; order++)
        {
            ICssStyleRule rule = _rules[order];

            bool matches;
            Priority specificity;
            try
            {
                matches = rule.TryMatch(element, null, out specificity);
            }
            catch (Exception)
            {
                continue; // Unsupported or oversized selector — degrade by skipping this rule.
            }

            if (!matches)
            {
                continue;
            }

            foreach (ICssProperty property in rule.Style)
            {
                into.Add(new CssDeclaration(property.Name, property.Value, property.IsImportant, specificity, order));
            }
        }
    }
}
