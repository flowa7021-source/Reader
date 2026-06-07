namespace Foliant.Rendering.Html;

/// <summary>
/// The three CSS generic font families the renderer maps tags and inline styles onto. Serif is the
/// default body family (matching browser UA defaults).
/// </summary>
public enum GenericFontFamily
{
    /// <summary>Serif body family (default) — Liberation Serif.</summary>
    Serif,

    /// <summary>Sans-serif family — Liberation Sans.</summary>
    SansSerif,

    /// <summary>Monospace family (code/pre) — Liberation Mono.</summary>
    Monospace,
}
