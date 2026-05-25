using Foliant.Domain;

namespace Foliant.Plugin.DjVu;

/// <summary>
/// Recognises DjVu by extension (<c>.djvu</c>/<c>.djv</c>) or the <c>AT&amp;T</c> magic bytes.
/// <see cref="LoadAsync"/> resolves the DjVuLibre CLI and opens the file out-of-process.
/// </summary>
public sealed class DjvuDocumentLoader : IDocumentLoader
{
    private static readonly byte[] Magic = "AT&T"u8.ToArray();

    public DocumentKind Kind => DocumentKind.Djvu;

    public bool CanLoad(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        string ext = Path.GetExtension(path);
        if (".djvu".Equals(ext, StringComparison.OrdinalIgnoreCase) ||
            ".djv".Equals(ext, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return HasDjvuMagic(path);
    }

    public async Task<IDocument> LoadAsync(string path, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("DjVu file not found.", path);
        }

        if (!DjvuToolset.TryResolve(out DjvuToolset? tools))
        {
            throw new InvalidOperationException(
                "DjVuLibre tools (ddjvu/djvused) were not found. Set " +
                @"HKCU\Software\Foliant\Plugins\DjVu to the DjVuLibre install directory, " +
                "or add it to PATH.");
        }

        return await DjvuDocument.OpenAsync(path, tools, ct).ConfigureAwait(false);
    }

    private static bool HasDjvuMagic(string path)
    {
        // FRAGILE: DjVu IFF — bytes 0..3 = "AT&T", 4..7 = "FORM"; we probe the leading "AT&T".
        Span<byte> head = stackalloc byte[Magic.Length];
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            int read = fs.Read(head);
            return read == Magic.Length && head.SequenceEqual(Magic);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
