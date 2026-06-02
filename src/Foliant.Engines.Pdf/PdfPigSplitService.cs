using System.Globalization;
using Foliant.Application.Services;
using UglyToad.PdfPig.Writer;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf;

/// <summary>
/// PdfPig-реализация <see cref="IPdfSplitService"/>: загружает source целиком в память,
/// нарезает/выбирает страницы через
/// <see cref="PdfMerger.Merge(IReadOnlyList{byte[]}, IReadOnlyList{IReadOnlyList{int}}, UglyToad.PdfPig.Writer.PdfAStandard, UglyToad.PdfPig.Writer.PdfDocumentBuilder.DocumentInformationBuilder)"/>
/// и пишет каждый target атомарно (tmp + Move), как <see cref="PdfPigPageRangeExtractor"/>.
///
/// Входные page-индексы 0-based; PdfPig pages — 1-based; конвертируем при построении списков.
/// </summary>
public sealed class PdfPigSplitService : IPdfSplitService
{
    public async Task<IReadOnlyList<string>> SplitEveryAsync(string sourcePath, int pagesPerChunk, string outputDirectory, string baseFileName, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseFileName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pagesPerChunk);

        byte[] source = await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);
        int pageCount = await Task.Run(() => CountPages(source), ct).ConfigureAwait(false);

        var paths = new List<string>();
        for (int firstPageIndex = 0, chunkNumber = 1; firstPageIndex < pageCount; firstPageIndex += pagesPerChunk, chunkNumber++)
        {
            int chunkSize = Math.Min(pagesPerChunk, pageCount - firstPageIndex);
            // Имя — 1-based, нулевой padding инвариантен культуре (никаких локализованных цифр).
            string fileName = string.Create(CultureInfo.InvariantCulture, $"{baseFileName}-{chunkNumber:D3}.pdf");
            string target = Path.Combine(outputDirectory, fileName);

            byte[] output = await Task.Run(() => MergeRange(source, firstPageIndex, chunkSize), ct).ConfigureAwait(false);
            await WriteAtomicAsync(target, output, ct).ConfigureAwait(false);
            paths.Add(Path.GetFullPath(target));
        }

        return paths;
    }

    public async Task ExtractSelectionAsync(string sourcePath, IReadOnlyList<int> pageIndices0Based, string targetPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(pageIndices0Based);
        if (pageIndices0Based.Count == 0)
        {
            throw new ArgumentException("Page selection must not be empty.", nameof(pageIndices0Based));
        }

        byte[] source = await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);
        byte[] output = await Task.Run(() => MergeSelection(source, pageIndices0Based), ct).ConfigureAwait(false);
        await WriteAtomicAsync(targetPath, output, ct).ConfigureAwait(false);
    }

    private static int CountPages(byte[] source)
    {
        using var doc = PdfPigDocument.Open(source);
        return doc.NumberOfPages;
    }

    private static byte[] MergeRange(byte[] source, int firstPageIndex, int count)
    {
        // PdfPig pages — 1-based; одна непрерывная выборка в естественном порядке.
        var pages = new int[count];
        for (int i = 0; i < count; i++)
        {
            pages[i] = firstPageIndex + i + 1;
        }

        return PdfMerger.Merge([source], [pages]);
    }

    private static byte[] MergeSelection(byte[] source, IReadOnlyList<int> pageIndices0Based)
    {
        int pageCount = CountPages(source);
        var pages = new int[pageIndices0Based.Count];
        for (int i = 0; i < pageIndices0Based.Count; i++)
        {
            int index = pageIndices0Based[i];
            ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(pageIndices0Based));
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, pageCount, nameof(pageIndices0Based));
            pages[i] = index + 1;
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
