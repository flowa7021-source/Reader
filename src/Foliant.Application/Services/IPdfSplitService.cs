namespace Foliant.Application.Services;

/// <summary>
/// Делит PDF на несколько файлов и извлекает произвольную (не-непрерывную) выборку
/// страниц — дополняет одно-диапазонный <see cref="IPageRangeExtractor"/> для операций
/// «split every N pages» и «cherry-pick pages». Page-индексы 0-based (совместимо с
/// <c>Bookmark.PageIndex</c>). Реализация stateless; открывает source, пишет каждый
/// target атомарно (через temp + Move).
///
/// Контракт ошибок:
/// <list type="bullet">
/// <item><see cref="SplitEveryAsync"/>: <c>pagesPerChunk</c> ≤ 0 →
/// <see cref="ArgumentOutOfRangeException"/>.</item>
/// <item><see cref="ExtractSelectionAsync"/>: пустой <c>pageIndices0Based</c>
/// → <see cref="ArgumentException"/>; любой индекс отрицательный или за пределами документа
/// → <see cref="ArgumentOutOfRangeException"/>.</item>
/// <item>Битый PDF / IO-сбой → пробрасывается caller'у (<see cref="System.IO.IOException"/>
/// или <see cref="InvalidOperationException"/>).</item>
/// </list>
/// </summary>
public interface IPdfSplitService
{
    /// <summary>
    /// Режет <paramref name="sourcePath"/> на куски по <paramref name="pagesPerChunk"/> страниц
    /// (последний — остаток) в <paramref name="outputDirectory"/>. Имена файлов —
    /// <c>{baseFileName}-001.pdf</c>, <c>{baseFileName}-002.pdf</c>… (1-based, нумерация
    /// инвариантна культуре). Возвращает абсолютные пути созданных файлов по порядку.
    /// </summary>
    Task<IReadOnlyList<string>> SplitEveryAsync(string sourcePath, int pagesPerChunk, string outputDirectory, string baseFileName, CancellationToken ct);

    /// <summary>
    /// Собирает один PDF из страниц <paramref name="sourcePath"/>, перечисленных в
    /// <paramref name="pageIndices0Based"/>, строго в указанном порядке (дубли запрещены —
    /// PdfPig не делит общий объект страницы между копиями). Пишет в <paramref name="targetPath"/>.
    /// </summary>
    Task ExtractSelectionAsync(string sourcePath, IReadOnlyList<int> pageIndices0Based, string targetPath, CancellationToken ct);
}
