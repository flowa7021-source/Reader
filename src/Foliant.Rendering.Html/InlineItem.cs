namespace Foliant.Rendering.Html;

/// <summary>
/// One atom of inline content gathered for a block: either a whitespace-collapsed word carrying its
/// resolved style, or a forced line break (<c>&lt;br&gt;</c>). Words are wrapped greedily; breaks
/// flush the current line unconditionally.
/// </summary>
internal sealed class InlineItem
{
    private InlineItem(string? word, ComputedStyle? style, bool isBreak)
    {
        Word = word;
        Style = style;
        IsBreak = isBreak;
    }

    /// <summary>The word text (no spaces), or <see langword="null"/> for a break.</summary>
    public string? Word { get; }

    /// <summary>The resolved style for the word, or <see langword="null"/> for a break.</summary>
    public ComputedStyle? Style { get; }

    /// <summary>Whether this item is a forced line break.</summary>
    public bool IsBreak { get; }

    /// <summary>Creates a word item.</summary>
    public static InlineItem ForWord(string word, ComputedStyle style) => new(word, style, isBreak: false);

    /// <summary>Creates a forced-break item.</summary>
    public static InlineItem Break { get; } = new(null, null, isBreak: true);
}
