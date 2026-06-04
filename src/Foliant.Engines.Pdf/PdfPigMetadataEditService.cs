using Foliant.Application.Services;
using Foliant.Domain;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Writer;
using DocumentInformationBuilder = UglyToad.PdfPig.Writer.PdfDocumentBuilder.DocumentInformationBuilder;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf;

/// <summary>
/// PdfPig-реализация <see cref="IPdfMetadataEditService"/>: загружает source целиком в память,
/// сливает его <c>/Info</c> со <see cref="PdfMetadataSpec"/> и ре-сериализует все страницы через
/// <see cref="PdfMerger.Merge(IReadOnlyList{byte[]}, IReadOnlyList{IReadOnlyList{int}}, PdfAStandard, DocumentInformationBuilder)"/>,
/// затем пишет target атомарно (tmp + Move), как <see cref="PdfPigSplitService"/>.
///
/// Merge-правило поля: <c>spec.X ?? source.Information.X</c> — <c>null</c> в spec сохраняет
/// текущее значение, пустая строка очищает (PdfPig пишет пустой <c>/Info</c>-entry), непустая
/// перезаписывает. <see cref="PdfMerger"/> всегда выставляет собственный <c>/Producer</c>, если
/// итог пуст — это ожидаемо для re-serialization tool.
/// </summary>
public sealed class PdfPigMetadataEditService : IPdfMetadataEditService
{
    public async Task EditAsync(string sourcePath, string targetPath, PdfMetadataSpec spec, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(spec);

        byte[] source = await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);
        byte[] output = await Task.Run(() => Rewrite(source, spec), ct).ConfigureAwait(false);
        await WriteAtomicAsync(targetPath, output, ct).ConfigureAwait(false);
    }

    private static byte[] Rewrite(byte[] source, PdfMetadataSpec spec)
    {
        int pageCount;
        DocumentInformationBuilder info;
        using (var doc = PdfPigDocument.Open(source))
        {
            pageCount = doc.NumberOfPages;
            info = MergeInfo(doc.Information, spec);
        }

        // PdfPig pages — 1-based; берём все страницы в естественном порядке (контент сохраняется).
        var pages = new int[pageCount];
        for (int i = 0; i < pageCount; i++)
        {
            pages[i] = i + 1;
        }

        return PdfMerger.Merge([source], [pages], PdfAStandard.None, info);
    }

    private static DocumentInformationBuilder MergeInfo(DocumentInformation src, PdfMetadataSpec spec) => new()
    {
        // spec-поле null → сохранить текущее значение source; иначе взять spec (включая "" = очистить).
        // null допустим в builder (PdfPig omit'ит поле из /Info) — поэтому null-forgiving на nullable-
        // oblivious сеттерах: merge-результат может быть null, если ни spec, ни source поля не задавали.
        Title = (spec.Title ?? src.Title)!,
        Author = (spec.Author ?? src.Author)!,
        Subject = (spec.Subject ?? src.Subject)!,
        Keywords = (spec.Keywords ?? src.Keywords)!,
        Creator = (spec.Creator ?? src.Creator)!,
        Producer = (spec.Producer ?? src.Producer)!,
    };

    private static async Task WriteAtomicAsync(string targetPath, byte[] bytes, CancellationToken ct)
    {
        // Cross-volume Move не атомарен — temp в той же папке, потом Move overwrite.
        string dir = Path.GetDirectoryName(Path.GetFullPath(targetPath))!;
        Directory.CreateDirectory(dir);
        string tmp = Path.Combine(dir, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(tmp, bytes, ct).ConfigureAwait(false);
            File.Move(tmp, targetPath, overwrite: true);
        }
        finally
        {
            TryDelete(tmp);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
        catch (UnauthorizedAccessException)
        {
            // best-effort cleanup
        }
    }
}
