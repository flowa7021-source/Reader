namespace Foliant.Application.Services;

/// <summary>
/// Вставляет ВСЕ страницы одного PDF в другой (Acrobat «Insert Pages») — managed-операция
/// над структурой документа, дополняющая <see cref="IPdfMergeService"/> точечной вставкой в
/// произвольную позицию. Page-индексы 0-based (совместимо с <c>Bookmark.PageIndex</c>).
/// Реализация stateless; читает оба файла целиком, source не мутируется, пишет target атомарно
/// (через temp + Move), поэтому <c>targetPath</c> может совпадать с <c>sourcePath</c>.
///
/// Контракт ошибок <see cref="InsertAsync"/>:
/// <list type="bullet">
/// <item>Пустой/whitespace <c>sourcePath</c>, <c>pdfToInsertPath</c> или <c>targetPath</c>
/// → <see cref="System.ArgumentException"/>.</item>
/// <item><c>insertAfterPageIndex</c> вне диапазона <c>[-1, sourcePageCount-1]</c>
/// → <see cref="System.ArgumentOutOfRangeException"/>.</item>
/// <item>Битый PDF / IO-сбой → пробрасывается caller'у (<see cref="System.IO.IOException"/>
/// или <see cref="System.InvalidOperationException"/>).</item>
/// </list>
/// </summary>
public interface IPdfInsertPagesService
{
    /// <summary>
    /// Вставляет ВСЕ страницы <paramref name="pdfToInsertPath"/> в <paramref name="sourcePath"/>
    /// сразу ПОСЛЕ 0-based страницы <paramref name="insertAfterPageIndex"/> и пишет результат в
    /// <paramref name="targetPath"/>. Итоговый порядок страниц: source <c>0..insertAfterPageIndex</c>,
    /// затем все вставляемые страницы, затем source <c>insertAfterPageIndex+1 .. конец</c>.
    /// Значение <c>-1</c> вставляет в самое начало (перед страницей 0);
    /// <c>sourcePageCount-1</c> добавляет в конец. Source-файл не изменяется.
    /// </summary>
    /// <param name="sourcePath">Путь к PDF, в который вставляют страницы.</param>
    /// <param name="insertAfterPageIndex">
    /// 0-based индекс страницы source, ПОСЛЕ которой вставлять; <c>-1</c> = в начало.
    /// Допустимый диапазон <c>[-1, sourcePageCount-1]</c>.
    /// </param>
    /// <param name="pdfToInsertPath">Путь к PDF, чьи страницы вставляются (все).</param>
    /// <param name="targetPath">
    /// Путь для записи результата; может совпадать с <paramref name="sourcePath"/>.
    /// </param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns><see cref="Task"/>, завершающийся после атомарной записи результата.</returns>
    /// <exception cref="System.ArgumentException">
    /// <paramref name="sourcePath"/>, <paramref name="pdfToInsertPath"/> или
    /// <paramref name="targetPath"/> пуст либо состоит из пробелов.
    /// </exception>
    /// <exception cref="System.ArgumentOutOfRangeException">
    /// <paramref name="insertAfterPageIndex"/> меньше <c>-1</c> либо
    /// больше или равен числу страниц source.
    /// </exception>
    Task InsertAsync(string sourcePath, int insertAfterPageIndex, string pdfToInsertPath, string targetPath, CancellationToken ct);
}
