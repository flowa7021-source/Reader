using Foliant.Application.Services;
using UglyToad.PdfPig.Writer;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf;

/// <summary>
/// PdfPig-реализация <see cref="IPageRangeExtractor"/>: загружает source целиком в память,
/// рендерит указанный диапазон через <see cref="PdfMerger.Merge(IReadOnlyList{byte[]}, IReadOnlyList{IReadOnlyList{int}}, PdfAStandard, PdfDocumentBuilder.DocumentInformationBuilder)"/>
/// и пишет в target атомарно (tmp + Move).
///
/// Bookmark.PageIndex — 0-based; PdfPig pages — 1-based; конвертируем при построении списка.
/// </summary>
public sealed class PdfPigPageRangeExtractor : IPageRangeExtractor
{
    public async Task ExtractAsync(string sourcePath, int firstPageIndex, int lastPageIndexInclusive, string targetPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentOutOfRangeException.ThrowIfNegative(firstPageIndex);
        ArgumentOutOfRangeException.ThrowIfLessThan(lastPageIndexInclusive, firstPageIndex);

        byte[] source = await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);
        byte[] output = await Task.Run(() => ExtractCore(source, firstPageIndex, lastPageIndexInclusive), ct).ConfigureAwait(false);
        await WriteAtomicAsync(targetPath, output, ct).ConfigureAwait(false);
    }

    private static byte[] ExtractCore(byte[] source, int firstPageIndex, int lastPageIndexInclusive)
    {
        int pageCount;
        using (var doc = PdfPigDocument.Open(source))
        {
            pageCount = doc.NumberOfPages;
        }

        if (lastPageIndexInclusive >= pageCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lastPageIndexInclusive),
                lastPageIndexInclusive,
                $"Last page index {lastPageIndexInclusive} exceeds document size {pageCount}.");
        }

        // PdfPig pages — 1-based. Один файл, одна выборка страниц в естественном порядке.
        int count = lastPageIndexInclusive - firstPageIndex + 1;
        var pages = new int[count];
        for (int i = 0; i < count; i++)
        {
            pages[i] = firstPageIndex + i + 1;
        }

        return PdfMerger.Merge([source], [pages]);
    }

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
