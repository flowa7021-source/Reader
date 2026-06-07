using System.Text;
using Foliant.Domain;
using Foliant.Rendering.Html;
using Microsoft.Extensions.Logging;

namespace Foliant.Engines.Mobi;

/// <summary>
/// Распознаёт MOBI по расширению (<c>.mobi</c>, <c>.prc</c>, <c>.azw</c>) либо по PalmDB
/// type/creator-сигнатуре «BOOKMOBI» в первых 68 байтах (sniff). Загружает через
/// <see cref="MobiDocument.Open(string, IHtmlRenderer)"/> — текстовые записи склеиваются,
/// разбиваются на главы и paginated/painted общим <see cref="IHtmlRenderer"/>.
///
/// Phase 1: только DRM-free PalmDOC-сжатый MOBI. HUFF/CDIC и AZW3 (KF8) — следующий PR.
/// </summary>
/// <param name="log">Logger for non-fatal load diagnostics.</param>
/// <param name="renderer">The shared HTML renderer passed to each opened document.</param>
public sealed class MobiDocumentLoader(ILogger<MobiDocumentLoader> log, IHtmlRenderer renderer) : IDocumentLoader
{
    // PalmDB type "BOOK" + creator "MOBI" at file offset 60 (смежно = "BOOKMOBI").
    private static readonly byte[] BookMobiBytes = Encoding.ASCII.GetBytes("BOOKMOBI");
    private const int TypeCreatorOffset = 60;

    public DocumentKind Kind => DocumentKind.Mobi;

    public bool CanLoad(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        string ext = Path.GetExtension(path);
        if (".mobi".Equals(ext, StringComparison.OrdinalIgnoreCase)
            || ".prc".Equals(ext, StringComparison.OrdinalIgnoreCase)
            || ".azw".Equals(ext, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return SniffMobi(path);
    }

    public async Task<IDocument> LoadAsync(string path, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("MOBI file not found.", path);
        }

        return await Task.Run<IDocument>(() =>
        {
            MobiDocument doc = MobiDocument.Open(path, renderer);
            log.LogDebug("Loaded MOBI '{Path}'", path);
            return doc;
        }, ct).ConfigureAwait(false);
    }

    private static bool SniffMobi(string path)
    {
        byte[] head = new byte[TypeCreatorOffset + BookMobiBytes.Length];
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

        if (read < TypeCreatorOffset + BookMobiBytes.Length)
        {
            return false;
        }

        return head.AsSpan(TypeCreatorOffset, BookMobiBytes.Length).SequenceEqual(BookMobiBytes);
    }
}
