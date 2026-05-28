using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Сериализует одну страницу документа в bitmap-файл (PNG/JPEG). Формат выбирается
/// реализацией по расширению <c>targetPath</c> — caller просто даёт путь. DPI кодируется в
/// файл через <c>zoom</c> (PageGeometry: 96 × zoom).
///
/// Документ-нейтрально: реализация рендерит через <see cref="IDocument.RenderPageAsync"/>
/// (что подключено в текущей вкладке), поэтому работает для PDF, DjVu, изображений и
/// любых будущих движков без изменения сигнатуры.
///
/// Контракт ошибок:
/// <list type="bullet">
/// <item>Неподдерживаемое расширение → <see cref="NotSupportedException"/>.</item>
/// <item>Out-of-range pageIndex / IO-сбой → пробрасывается caller'у.</item>
/// </list>
/// </summary>
public interface IPageImageExporter
{
    /// <summary>Список расширений (lowercase, без точки), которые реализация может писать.
    /// UI строит из этого dialog-фильтр.</summary>
    IReadOnlyList<string> SupportedFormats { get; }

    Task ExportAsync(IDocument document, int pageIndex, double zoom, string targetPath, CancellationToken ct);

    /// <summary>Batch-вариант: рендерит и пишет диапазон страниц в указанную папку. Имена файлов —
    /// <c>{namePrefix}-p{NNN}.{format}</c> с zero-padded шириной по числу страниц диапазона.
    /// <paramref name="progress"/> репортит количество завершённых страниц (для UI бара). Останов
    /// по cancellation token проверяется между страницами. Возвращает фактически записанное
    /// количество (= диапазон при успехе, &lt; при ранней отмене).
    ///
    /// Контракт: <paramref name="targetDirectory"/> создаётся при отсутствии; существующие файлы
    /// перезаписываются (по тому же atomic temp + Move паттерну, что у single-page).</summary>
    Task<int> ExportRangeAsync(
        IDocument document,
        int firstPageIndex,
        int lastPageIndexInclusive,
        double zoom,
        string targetDirectory,
        string format,
        string namePrefix,
        IProgress<int>? progress,
        CancellationToken ct);
}
