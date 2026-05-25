using Foliant.Domain;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Writer;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Операции над <b>структурой</b> PDF-файла (удаление/переупорядочивание/вставка/поворот
/// страниц), порождающие <b>новый</b> PDF. Реализовано поверх writer-слоя PdfPig
/// (<see cref="PdfMerger"/> и <see cref="PdfDocumentBuilder"/>).
///
/// Каждая операция читает входной файл и возвращает новый документ как <c>byte[]</c>
/// (in-memory, удобно для тестов) либо атомарно пишет его в выходной путь
/// (temp + <see cref="File.Move(string, string, bool)"/>). Индексы — 0-based; выход
/// за границы → <see cref="ArgumentOutOfRangeException"/>.
///
/// Чистая индексная арифметика вынесена в <see cref="PageOrder"/> и покрыта unit-тестами
/// без нативного слоя. PdfPig — чистый managed-код, поэтому сами операции синхронны;
/// публичный async-API оборачивает их в <see cref="Task.Run{TResult}(Func{TResult}, CancellationToken)"/>
/// ради неблокирующего IO и поддержки отмены.
/// </summary>
public static class PdfPageOps
{
    public static Task<byte[]> DeletePageAsync(string inputPath, int index, CancellationToken ct) =>
        RunAsync(inputPath, bytes => DeletePageCore(bytes, index), ct);

    public static Task DeletePageAsync(string inputPath, string outputPath, int index, CancellationToken ct) =>
        RunAsync(inputPath, outputPath, bytes => DeletePageCore(bytes, index), ct);

    public static Task<byte[]> ReorderPagesAsync(string inputPath, int[] newOrder, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(newOrder);
        var copy = (int[])newOrder.Clone();
        return RunAsync(inputPath, bytes => ReorderCore(bytes, copy), ct);
    }

    public static Task ReorderPagesAsync(string inputPath, string outputPath, int[] newOrder, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(newOrder);
        var copy = (int[])newOrder.Clone();
        return RunAsync(inputPath, outputPath, bytes => ReorderCore(bytes, copy), ct);
    }

    public static async Task<byte[]> InsertPagesAsync(
        string inputPath, string otherPdfPath, int atIndex, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(otherPdfPath);
        byte[] other = await ReadAllAsync(otherPdfPath, ct).ConfigureAwait(false);
        return await RunAsync(inputPath, bytes => InsertCore(bytes, other, atIndex), ct).ConfigureAwait(false);
    }

    public static async Task InsertPagesAsync(
        string inputPath, string otherPdfPath, string outputPath, int atIndex, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(otherPdfPath);
        byte[] other = await ReadAllAsync(otherPdfPath, ct).ConfigureAwait(false);
        await RunAsync(inputPath, outputPath, bytes => InsertCore(bytes, other, atIndex), ct).ConfigureAwait(false);
    }

    public static Task<byte[]> RotatePageAsync(string inputPath, int index, ViewRotation rotation, CancellationToken ct) =>
        RunAsync(inputPath, bytes => RotateCore(bytes, index, rotation), ct);

    public static Task RotatePageAsync(
        string inputPath, string outputPath, int index, ViewRotation rotation, CancellationToken ct) =>
        RunAsync(inputPath, outputPath, bytes => RotateCore(bytes, index, rotation), ct);

    // Синхронные byte[]→byte[] трансформации для event-sourced редактора
    // (PdfDocumentEditor.Dispatch). PdfPig — managed-код, поэтому операции синхронны;
    // редактор сам выносит их в Task.Run и владеет IO/persist-границами.
    public static byte[] DeletePage(byte[] input, int index)
    {
        ArgumentNullException.ThrowIfNull(input);
        return DeletePageCore(input, index);
    }

    public static byte[] ReorderPages(byte[] input, int[] newOrder)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(newOrder);
        return ReorderCore(input, (int[])newOrder.Clone());
    }

    public static byte[] InsertPages(byte[] baseDoc, byte[] other, int atIndex)
    {
        ArgumentNullException.ThrowIfNull(baseDoc);
        ArgumentNullException.ThrowIfNull(other);
        return InsertCore(baseDoc, other, atIndex);
    }

    public static byte[] RotatePage(byte[] input, int index, ViewRotation rotation)
    {
        ArgumentNullException.ThrowIfNull(input);
        return RotateCore(input, index, rotation);
    }

    private static byte[] DeletePageCore(byte[] input, int index)
    {
        int pageCount = CountPages(input);
        int[] keep = PageOrder.BuildAfterDelete(pageCount, index);
        return MergeSelection([input], [keep]);
    }

    private static byte[] ReorderCore(byte[] input, int[] newOrder)
    {
        int pageCount = CountPages(input);
        int[] order = PageOrder.BuildReorder(pageCount, newOrder);
        return MergeSelection([input], [order]);
    }

    private static byte[] InsertCore(byte[] baseDoc, byte[] other, int atIndex)
    {
        int basePages = CountPages(baseDoc);
        int otherPages = CountPages(other);

        int[] before = PageOrder.BasePagesBefore(basePages, atIndex);
        int[] after = PageOrder.BasePagesAfter(basePages, atIndex);
        int[] inserted = PageOrder.AllPages(otherPages);

        // Базовый документ участвует дважды (до и после точки вставки), вставляемый — между
        // ними. PdfMerger конкатенирует выборки в порядке files[]; так получаем чередование.
        return MergeSelection([baseDoc, other, baseDoc], [before, inserted, after]);
    }

    private static byte[] RotateCore(byte[] input, int index, ViewRotation rotation)
    {
        // FRAGILE: PdfMerger не умеет поворот — используем PdfDocumentBuilder.AddPage + SetRotation.
        using var src = PdfPigDocument.Open(input);
        int pageCount = src.NumberOfPages;
        if (index < 0 || index >= pageCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index), index, $"Page index must be in [0, {pageCount - 1}].");
        }

        using var builder = new PdfDocumentBuilder();
        for (int pageNo = 1; pageNo <= pageCount; pageNo++)
        {
            var pageBuilder = builder.AddPage(src, pageNo);
            if (pageNo - 1 == index)
            {
                pageBuilder.SetRotation(ComposeRotation(src, pageNo, rotation));
            }
        }

        return builder.Build();
    }

    private static PageRotationDegrees ComposeRotation(PdfPigDocument doc, int pageNumber, ViewRotation rotation)
    {
        // AddPage копирует исходный /Rotate, но SetRotation перезаписывает его БЕЗУСЛОВНО.
        // Поэтому складываем существующий поворот страницы с запрошенным и нормализуем
        // в {0,90,180,270}, иначе исходная ориентация была бы потеряна.
        int existing = doc.GetPage(pageNumber).Rotation.Value;
        int total = ((existing + rotation.Degrees()) % 360 + 360) % 360;
        return new PageRotationDegrees(total);
    }

    private static byte[] MergeSelection(IReadOnlyList<byte[]> files, IReadOnlyList<IReadOnlyList<int>> pages) =>
        PdfMerger.Merge(files, pages);

    private static int CountPages(byte[] pdf)
    {
        using var doc = PdfPigDocument.Open(pdf);
        return doc.NumberOfPages;
    }

    private static async Task<byte[]> RunAsync(string inputPath, Func<byte[], byte[]> op, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(inputPath);
        byte[] input = await ReadAllAsync(inputPath, ct).ConfigureAwait(false);
        return await Task.Run(() => op(input), ct).ConfigureAwait(false);
    }

    private static async Task RunAsync(
        string inputPath, string outputPath, Func<byte[], byte[]> op, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(inputPath);
        ArgumentNullException.ThrowIfNull(outputPath);
        byte[] input = await ReadAllAsync(inputPath, ct).ConfigureAwait(false);
        byte[] output = await Task.Run(() => op(input), ct).ConfigureAwait(false);
        await WriteAtomicAsync(outputPath, output, ct).ConfigureAwait(false);
    }

    private static async Task WriteAtomicAsync(string outputPath, byte[] bytes, CancellationToken ct)
    {
        // FRAGILE: atomic write — temp в той же папке (cross-volume Move не атомарен), затем File.Move overwrite.
        // finally чистит temp только если Move его не «съел» (исключение/отмена до Move).
        string dir = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
        Directory.CreateDirectory(dir);
        string tmp = Path.Combine(dir, $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(tmp, bytes, ct).ConfigureAwait(false);
            File.Move(tmp, outputPath, overwrite: true);
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
            // best-effort cleanup of the temp file; nothing actionable on failure.
        }
        catch (UnauthorizedAccessException)
        {
            // best-effort cleanup of the temp file; nothing actionable on failure.
        }
    }

    private static Task<byte[]> ReadAllAsync(string path, CancellationToken ct) =>
        File.ReadAllBytesAsync(path, ct);
}
