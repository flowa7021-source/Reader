using Foliant.Rendering.Html;
using VersOne.Epub;

namespace Foliant.Engines.Epub;

/// <summary>
/// Resolves <c>&lt;img src&gt;</c> references inside one EPUB spine item to the raw encoded image
/// bytes held by VersOne.Epub. The renderer owns no I/O, so each render request gets one of these
/// bound to the current chapter's base path (used to turn a chapter-relative <c>src</c> into the
/// archive-absolute path VersOne keys images by).
///
/// <para>VersOne.Epub 3.3.4 keying note: <see cref="EpubContentFile.Key"/> is the path
/// <em>relative to the OPF root file directory</em> (e.g. <c>img/pic.png</c>), whereas
/// <c>EpubLocalContentFile.FilePath</c> is the path from the archive root (e.g.
/// <c>OEBPS/img/pic.png</c>). A spine item's <c>FilePath</c> (e.g. <c>OEBPS/chapter1.xhtml</c>) plus
/// a chapter-relative <c>src</c> therefore yields the image's <c>FilePath</c>; the bare <c>src</c>
/// itself often equals the image's <c>Key</c>. We try both, plus a bare-filename fallback, because
/// real-world EPUBs vary.</para>
/// </summary>
internal sealed class EpubResourceResolver : IResourceResolver
{
    private readonly EpubBook _book;
    private readonly string _spineDirectory;

    /// <summary>Creates a resolver bound to one spine item.</summary>
    /// <param name="book">The loaded EPUB whose <c>Content.Images</c> backs resolution.</param>
    /// <param name="spineFilePath">The archive-absolute <c>FilePath</c> of the spine item being
    /// rendered; used to anchor chapter-relative <c>src</c> values.</param>
    public EpubResourceResolver(EpubBook book, string? spineFilePath)
    {
        ArgumentNullException.ThrowIfNull(book);
        _book = book;
        _spineDirectory = NormalizeSlashes(Path.GetDirectoryName(spineFilePath ?? string.Empty) ?? string.Empty);
    }

    /// <inheritdoc/>
    public bool TryResolveImage(string src, out ReadOnlyMemory<byte> bytes)
    {
        bytes = ReadOnlyMemory<byte>.Empty;
        if (string.IsNullOrWhiteSpace(src))
        {
            return false;
        }

        string cleaned = StripFragmentAndQuery(src);
        if (cleaned.Length == 0)
        {
            return false;
        }

        // Data URIs are unsupported (deferred): there is no inline-data decode path, so they are
        // skipped here (the renderer drops any <img> whose resolver returns false).
        if (cleaned.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string decoded = NormalizeSlashes(Uri.UnescapeDataString(cleaned));

        // Candidate 1: chapter-relative src combined with the spine item's directory → archive-absolute
        // path, which matches the image's FilePath.
        string combined = NormalizePathSegments(Combine(_spineDirectory, decoded));

        // Candidate 2: the src as-is (already absolute, or matching the image Key relative to the OPF root).
        string asIs = NormalizePathSegments(decoded);

        // Candidate 3: the bare filename, for EPUBs whose manifest flattens paths.
        string bareName = GetFileName(asIs);

        return TryLookup(combined, out bytes)
            || TryLookup(asIs, out bytes)
            || TryLookup(bareName, out bytes);
    }

    /// <inheritdoc/>
    public bool TryResolveCss(string href, out string css)
    {
        css = string.Empty;
        if (string.IsNullOrWhiteSpace(href))
        {
            return false;
        }

        string cleaned = StripFragmentAndQuery(href);
        if (cleaned.Length == 0 || cleaned.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string decoded = NormalizeSlashes(Uri.UnescapeDataString(cleaned));

        // Same candidate resolution as images: chapter-relative → archive-absolute (FilePath), the
        // href as-is (Key relative to the OPF root), then the bare filename for flattened manifests.
        string combined = NormalizePathSegments(Combine(_spineDirectory, decoded));
        string asIs = NormalizePathSegments(decoded);
        string bareName = GetFileName(asIs);

        return TryLookupCss(combined, out css)
            || TryLookupCss(asIs, out css)
            || TryLookupCss(bareName, out css);
    }

    /// <summary>Looks a candidate path up against both the FilePath and Key indices of the CSS
    /// collection, returning the stylesheet text on a hit.</summary>
    private bool TryLookupCss(string candidate, out string css)
    {
        css = string.Empty;
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        EpubContentCollection<EpubLocalTextContentFile, EpubRemoteTextContentFile> sheets = _book.Content.Css;

        if (sheets.TryGetLocalFileByFilePath(candidate, out EpubLocalTextContentFile? byFilePath) && byFilePath?.Content is { } a)
        {
            css = a;
            return true;
        }

        if (sheets.TryGetLocalFileByKey(candidate, out EpubLocalTextContentFile? byKey) && byKey?.Content is { } b)
        {
            css = b;
            return true;
        }

        return false;
    }

    /// <summary>Looks a candidate path up against both the FilePath and Key indices of the image
    /// collection.</summary>
    private bool TryLookup(string candidate, out ReadOnlyMemory<byte> bytes)
    {
        bytes = ReadOnlyMemory<byte>.Empty;
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        EpubContentCollection<EpubLocalByteContentFile, EpubRemoteByteContentFile> images = _book.Content.Images;

        if (images.TryGetLocalFileByFilePath(candidate, out EpubLocalByteContentFile? byFilePath) && byFilePath?.Content is { } a)
        {
            bytes = a;
            return true;
        }

        if (images.TryGetLocalFileByKey(candidate, out EpubLocalByteContentFile? byKey) && byKey?.Content is { } b)
        {
            bytes = b;
            return true;
        }

        return false;
    }

    /// <summary>Strips a trailing <c>#fragment</c> and/or <c>?query</c> from a raw src.</summary>
    private static string StripFragmentAndQuery(string src)
    {
        int hash = src.IndexOf('#', StringComparison.Ordinal);
        if (hash >= 0)
        {
            src = src[..hash];
        }

        int query = src.IndexOf('?', StringComparison.Ordinal);
        if (query >= 0)
        {
            src = src[..query];
        }

        return src.Trim();
    }

    private static string Combine(string directory, string relative)
    {
        if (relative.StartsWith('/'))
        {
            // Absolute (archive-rooted) reference — ignore the chapter directory.
            return relative.TrimStart('/');
        }

        return directory.Length == 0 ? relative : directory + "/" + relative;
    }

    /// <summary>Collapses <c>.</c> and <c>..</c> segments and removes empty segments, using forward
    /// slashes (the separator VersOne keys content by).</summary>
    private static string NormalizePathSegments(string path)
    {
        if (path.Length == 0)
        {
            return path;
        }

        string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var stack = new List<string>(parts.Length);
        foreach (string part in parts)
        {
            if (part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (stack.Count > 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                continue;
            }

            stack.Add(part);
        }

        return string.Join('/', stack);
    }

    private static string GetFileName(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }

    private static string NormalizeSlashes(string path) => path.Replace('\\', '/');
}
