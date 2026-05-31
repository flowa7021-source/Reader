using System.Text;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.Engines.Fb2;

/// <summary>
/// Распознаёт FB2 по расширению (<c>.fb2</c>) либо по XML-magic + наличию строки
/// <c>FictionBook</c> в первых ~512 байтах (sniff). Загружает через <see cref="Fb2Document.Open"/>.
///
/// Phase 1: только нескомпрессованные .fb2. <c>.fb2.zip</c> и <c>.fbz</c> (gzipped) — следующий PR.
/// </summary>
public sealed class Fb2DocumentLoader(ILogger<Fb2DocumentLoader> log) : IDocumentLoader
{
    /// <summary>Sniff-bytes: ищем подстроку «FictionBook» в начале XML-файла.</summary>
    private static readonly byte[] FictionBookBytes = Encoding.ASCII.GetBytes("FictionBook");

    public DocumentKind Kind => DocumentKind.Fb2;

    public bool CanLoad(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        string ext = Path.GetExtension(path);
        if (".fb2".Equals(ext, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return SniffFb2(path);
    }

    public async Task<IDocument> LoadAsync(string path, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("FB2 file not found.", path);
        }

        return await Task.Run<IDocument>(() =>
        {
            Fb2Document doc = Fb2Document.Open(path);
            log.LogDebug("Loaded FB2 '{Path}'", path);
            return doc;
        }, ct).ConfigureAwait(false);
    }

    private static bool SniffFb2(string path)
    {
        byte[] head = new byte[512];
        int read;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            read = fs.Read(head, 0, head.Length);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        if (read < FictionBookBytes.Length)
        {
            return false;
        }

        // Поиск "FictionBook" в первых байтах — достаточный sniff для XML root tag.
        for (int i = 0; i <= read - FictionBookBytes.Length; i++)
        {
            if (head.AsSpan(i, FictionBookBytes.Length).SequenceEqual(FictionBookBytes))
            {
                return true;
            }
        }
        return false;
    }
}
