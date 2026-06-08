namespace Foliant.Rendering.Html;

/// <summary>
/// Resolves <c>&lt;img&gt;</c> sources to raw encoded image bytes (PNG/JPEG/BMP/...). The
/// renderer owns no I/O: the hosting engine (EPUB/FB2/MOBI) implements this to pull bytes out of
/// the container by the <c>src</c> attribute. Decoding is the renderer's job.
/// </summary>
public interface IResourceResolver
{
    /// <summary>
    /// Attempts to resolve an image referenced by an <c>&lt;img src="..."&gt;</c> attribute.
    /// </summary>
    /// <param name="src">The raw <c>src</c> attribute value (relative path, fragment, data-uri, ...).</param>
    /// <param name="bytes">On success, the encoded image bytes; otherwise <see cref="ReadOnlyMemory{T}.Empty"/>.</param>
    /// <returns><see langword="true"/> if the image was resolved; otherwise <see langword="false"/>.</returns>
    bool TryResolveImage(string src, out ReadOnlyMemory<byte> bytes);

    /// <summary>
    /// Attempts to resolve a stylesheet referenced by a <c>&lt;link rel="stylesheet" href="..."&gt;</c>
    /// element to its raw CSS text. The renderer applies the author cascade (selectors + specificity)
    /// over its user-agent defaults and under inline <c>style</c> attributes.
    ///
    /// <para>The default implementation resolves nothing (returns <see langword="false"/>): only
    /// container-backed resolvers (e.g. EPUB) override it to pull linked CSS out of the archive.
    /// <c>&lt;style&gt;</c> blocks embedded directly in the chapter markup are read from the parsed DOM
    /// and never flow through this method.</para>
    /// </summary>
    /// <param name="href">The raw <c>href</c> attribute value (relative path, fragment, ...).</param>
    /// <param name="css">On success, the stylesheet text; otherwise the empty string.</param>
    /// <returns><see langword="true"/> if the stylesheet was resolved; otherwise <see langword="false"/>.</returns>
    bool TryResolveCss(string href, out string css)
    {
        css = string.Empty;
        return false;
    }
}

/// <summary>
/// An <see cref="IResourceResolver"/> that resolves nothing — every <c>&lt;img&gt;</c> is skipped.
/// Useful for text-only rendering and as a safe default.
/// </summary>
public sealed class NullResourceResolver : IResourceResolver
{
    /// <summary>The shared singleton instance.</summary>
    public static NullResourceResolver Instance { get; } = new();

    /// <inheritdoc/>
    public bool TryResolveImage(string src, out ReadOnlyMemory<byte> bytes)
    {
        bytes = ReadOnlyMemory<byte>.Empty;
        return false;
    }
}
