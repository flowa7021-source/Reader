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
