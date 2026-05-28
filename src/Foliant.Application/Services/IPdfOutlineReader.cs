using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Читает древовидное PDF /Outlines (содержание) из файла и возвращает плоский список
/// <see cref="DocumentOutlineEntry"/> с пометкой глубины. Нужен для «Import PDF outline as
/// bookmarks» — большинство PDF поставляются с outline'ом, но наш sidecar bookmark-store
/// о нём не знает, пока пользователь явно не запросит импорт.
///
/// Stateless, делегирует чтение реализации; ошибки парсинга PDF не пробрасываются — возвращается
/// пустой список (лог пишет реализация).
/// </summary>
public interface IPdfOutlineReader
{
    /// <summary>Прочитать /Outlines из PDF. Если outline отсутствует или PDF битый — пустой
    /// список (не исключение). URI/external destinations пропускаются, остаются только
    /// page-bound записи (с валидным <see cref="DocumentOutlineEntry.PageIndex"/>).</summary>
    Task<IReadOnlyList<DocumentOutlineEntry>> ReadAsync(string pdfPath, CancellationToken ct);
}
