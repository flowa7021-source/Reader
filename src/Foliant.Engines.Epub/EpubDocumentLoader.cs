using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.Engines.Epub;

/// <summary>
/// Распознаёт EPUB по расширению либо по ZIP-magic + наличию <c>mimetype</c> файла с
/// контентом <c>application/epub+zip</c> (минимальная sanity-проверка). Загружает через
/// <see cref="EpubDocument.Open"/> — VersOne.Epub разворачивает контейнер и spine.
/// </summary>
public sealed class EpubDocumentLoader(ILogger<EpubDocumentLoader> log) : IDocumentLoader
{
    /// <summary>ZIP local-file-header magic (<c>PK\x03\x04</c>).</summary>
    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04];

    /// <summary>EPUB-specific bytes сразу после <c>mimetype</c> file header в un-compressed ZIP.
    /// Минимальная sanity-проверка для не-расширённых EPUB-файлов.</summary>
    private static readonly byte[] EpubMimetypeBytes = System.Text.Encoding.ASCII.GetBytes("application/epub+zip");

    public DocumentKind Kind => DocumentKind.Epub;

    public bool CanLoad(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        string ext = Path.GetExtension(path);
        if (".epub".Equals(ext, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Fallback: extension-less но это ZIP-контейнер с epub-mimetype в первых ~120 байтах
        // (EPUB-spec требует mimetype файлом без компрессии в начале архива).
        return SniffEpub(path);
    }

    public async Task<IDocument> LoadAsync(string path, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("EPUB file not found.", path);
        }

        return await Task.Run<IDocument>(() =>
        {
            EpubDocument doc = EpubDocument.Open(path);
            log.LogDebug("Loaded EPUB '{Path}' via VersOne.Epub", path);
            return doc;
        }, ct).ConfigureAwait(false);
    }

    private static bool SniffEpub(string path)
    {
        Span<byte> head = stackalloc byte[128];
        int read;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            read = fs.Read(head);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        if (read < ZipMagic.Length || !head[..ZipMagic.Length].SequenceEqual(ZipMagic))
        {
            return false;
        }

        // Поиск "application/epub+zip" в первых 128 байтах — типичное расположение для
        // EPUB (mimetype файл первый в архиве, без компрессии, сразу за local header).
        if (read < EpubMimetypeBytes.Length)
        {
            return false;
        }

        for (int i = 0; i <= read - EpubMimetypeBytes.Length; i++)
        {
            if (head.Slice(i, EpubMimetypeBytes.Length).SequenceEqual(EpubMimetypeBytes))
            {
                return true;
            }
        }
        return false;
    }
}
