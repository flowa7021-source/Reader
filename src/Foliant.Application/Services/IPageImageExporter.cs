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
}
