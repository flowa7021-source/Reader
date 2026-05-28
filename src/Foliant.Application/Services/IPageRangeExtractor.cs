namespace Foliant.Application.Services;

/// <summary>
/// Извлекает непрерывный диапазон страниц PDF в новый файл — для функциональности
/// «extract chapter by bookmark» и подобных. Page-индексы 0-based (совместимо с
/// <c>Bookmark.PageIndex</c>); диапазон inclusive с обеих сторон. Реализация stateless;
/// открывает source, пишет target атомарно (через temp + Move).
///
/// Контракт ошибок:
/// <list type="bullet">
/// <item>Invalid range (start &gt; end, отрицательные значения, выход за границы документа)
/// → <see cref="ArgumentOutOfRangeException"/>.</item>
/// <item>Битый PDF / IO-сбой → пробрасывается caller'у (<see cref="System.IO.IOException"/>
/// или <see cref="InvalidOperationException"/>).</item>
/// </list>
/// </summary>
public interface IPageRangeExtractor
{
    Task ExtractAsync(string sourcePath, int firstPageIndex, int lastPageIndexInclusive, string targetPath, CancellationToken ct);
}
