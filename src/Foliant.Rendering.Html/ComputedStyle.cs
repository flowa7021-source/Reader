using SixLabors.ImageSharp;

namespace Foliant.Rendering.Html;

/// <summary>Horizontal text alignment within the content column.</summary>
internal enum TextAlign
{
    Left,
    Center,
    Right,
}

/// <summary>
/// The fully-resolved (inherited + inline-overridden) style of one element, in CSS pixels
/// (i.e. <em>before</em> the viewport scale is applied). Inheritable properties are the font
/// triplet, colour and alignment; box properties (display/margins/indent/list) are per-element.
/// </summary>
internal sealed record ComputedStyle
{
    /// <summary>Inheritable: generic font family.</summary>
    public GenericFontFamily Family { get; init; } = GenericFontFamily.Serif;

    /// <summary>Inheritable: bold weight.</summary>
    public bool Bold { get; init; }

    /// <summary>Inheritable: italic slant.</summary>
    public bool Italic { get; init; }

    /// <summary>Inheritable: font size in CSS pixels (unscaled).</summary>
    public double FontSizePx { get; init; } = 16.0;

    /// <summary>Inheritable: fill colour.</summary>
    public Color Color { get; init; } = Color.Black;

    /// <summary>Inheritable: horizontal alignment.</summary>
    public TextAlign Align { get; init; } = TextAlign.Left;

    /// <summary>Box: whether the element starts a new block (vs inline flow).</summary>
    public bool IsBlock { get; init; }

    /// <summary>Box: <c>display:none</c> — the element and its entire subtree are not rendered. Not
    /// inherited (the subtree is simply never visited, so children never resolve a style).</summary>
    public bool Hidden { get; init; }

    /// <summary>Box: top margin in CSS pixels (unscaled).</summary>
    public double MarginTopPx { get; init; }

    /// <summary>Box: bottom margin in CSS pixels (unscaled).</summary>
    public double MarginBottomPx { get; init; }

    /// <summary>Box: extra left indent in CSS pixels (unscaled), additive with the parent's indent.</summary>
    public double IndentPx { get; init; }

    /// <summary>The list kind this element introduces, if any (drives marker rendering for <c>li</c>).</summary>
    public ListKind List { get; init; } = ListKind.None;

    /// <summary>Whether this element forces a line break before the following inline content (<c>br</c>).</summary>
    public bool ForcesBreak { get; init; }

    /// <summary>Produces the child's inherited baseline (font triplet, colour, alignment, list context).</summary>
    public ComputedStyle InheritTo() => new()
    {
        Family = Family,
        Bold = Bold,
        Italic = Italic,
        FontSizePx = FontSizePx,
        Color = Color,
        Align = Align,
        List = List,
        // Box properties do not inherit.
    };
}

/// <summary>The kind of list an element establishes for its <c>li</c> children.</summary>
internal enum ListKind
{
    None,
    Unordered,
    Ordered,
}
