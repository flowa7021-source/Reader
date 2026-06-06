using Foliant.Application.Services;
using UglyToad.PdfPig.Writer;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf;

/// <summary>
/// PdfPig-реализация <see cref="IPdfInsertPagesService"/>: читает source и вставляемый файл
/// целиком в память и собирает результат через
/// <see cref="PdfMerger.Merge(IReadOnlyList{byte[]}, IReadOnlyList{IReadOnlyList{int}}, UglyToad.PdfPig.Writer.PdfAStandard, UglyToad.PdfPig.Writer.PdfDocumentBuilder.DocumentInformationBuilder)"/>,
/// затем пишет target атомарно (tmp + Move), как <see cref="PdfPigSplitService"/>.
///
/// Этот overload <see cref="PdfMerger"/> эмитит страницы в порядке СПИСКА файлов: для каждого
/// файла (по индексу) добавляются его страницы из соответствующего page-bundle (1-based), и
/// одинаковые байты, переданные на разных позициях, трактуются как независимые документы. Поэтому
/// для вставки передаём <c>[source, insert, source]</c> с bundles
/// <c>[ [1..k+1], [1..insertCount], [k+2..n] ]</c> (k = <c>insertAfterPageIndex</c>, 0-based;
/// n = число страниц source) — итог в точности source <c>0..k</c> + вставка + source <c>k+1..end</c>.
/// Пустые сегменты (например голова при <c>insertAfterPageIndex == -1</c> или хвост при вставке в
/// конец) опускаются: <see cref="PdfMerger"/> копирует ВСЕ страницы файла при <c>null</c>-bundle, а
/// здесь пустой сегмент означает «ноль страниц», поэтому такой файл просто не добавляем в список.
///
/// Входной page-индекс 0-based; PdfPig pages — 1-based; конвертируем при построении списков.
/// </summary>
public sealed class PdfPigInsertPagesService : IPdfInsertPagesService
{
    /// <inheritdoc/>
    public async Task InsertAsync(string sourcePath, int insertAfterPageIndex, string pdfToInsertPath, string targetPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfToInsertPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        // Читаем оба файла целиком ДО любой записи — source==target безопасен, source не мутируется.
        byte[] source = await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);
        byte[] insert = await File.ReadAllBytesAsync(pdfToInsertPath, ct).ConfigureAwait(false);

        byte[] output = await Task.Run(() => MergeInsert(source, insert, insertAfterPageIndex), ct).ConfigureAwait(false);
        await WriteAtomicAsync(targetPath, output, ct).ConfigureAwait(false);
    }

    private static byte[] MergeInsert(byte[] source, byte[] insert, int insertAfterPageIndex)
    {
        int sourcePageCount = CountPages(source);
        // Допустимо [-1, sourcePageCount-1]: -1 = в начало, sourcePageCount-1 = в конец.
        ArgumentOutOfRangeException.ThrowIfLessThan(insertAfterPageIndex, -1);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(insertAfterPageIndex, sourcePageCount);

        int insertPageCount = CountPages(insert);

        // PdfMerger эмитит в порядке списка файлов. Голова: source-страницы 1..(k+1) (0-based 0..k);
        // вставка: все страницы insert; хвост: source-страницы (k+2)..n (0-based k+1..end).
        int headCount = insertAfterPageIndex + 1;       // число source-страниц до точки вставки
        int tailStart1Based = insertAfterPageIndex + 2; // первая source-страница после точки вставки (1-based)

        var files = new List<byte[]>(3);
        var bundles = new List<IReadOnlyList<int>>(3);

        // Пустые сегменты не добавляем: пустой bundle = ноль страниц, лишний файл открывать незачем,
        // а null-bundle у PdfMerger означал бы «все страницы» — что здесь неверно.
        if (headCount > 0)
        {
            files.Add(source);
            bundles.Add(Range1Based(1, headCount));
        }

        if (insertPageCount > 0)
        {
            files.Add(insert);
            bundles.Add(Range1Based(1, insertPageCount));
        }

        int tailCount = sourcePageCount - tailStart1Based + 1;
        if (tailCount > 0)
        {
            files.Add(source);
            bundles.Add(Range1Based(tailStart1Based, tailCount));
        }

        return PdfMerger.Merge(files, bundles);
    }

    private static int[] Range1Based(int firstPage, int count)
    {
        var pages = new int[count];
        for (int i = 0; i < count; i++)
        {
            pages[i] = firstPage + i;
        }

        return pages;
    }

    private static int CountPages(byte[] bytes)
    {
        using var doc = PdfPigDocument.Open(bytes);
        return doc.NumberOfPages;
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
