using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Чтение списка слоёв (OCG, Optional Content Groups) PDF-документа + переключение
/// видимости конкретных слоёв (запись инкрементальным апдейтом в новый файл).
/// Phase 2 MVP (Q-F8): только flat-список + on/off. Не покрывает создание/удаление
/// слоёв, перемещение объектов между слоями, иерархию (parent/child), OCMD и сложные
/// visibility-правила (VE) — оставлено на Phase 2+/3.
///
/// Реализации — managed-only (PdfPig + raw cos-write), без зависимости от native PDFium
/// OCG-bindings (которых нет в PDFiumCore 146.x). Pdfium-rendering сам подхватывает
/// обновлённое состояние слоёв из <c>/OCProperties → /D</c> при загрузке PDF.
///
/// Контракт ошибок:
/// <list type="bullet">
/// <item>Пустые / blank пути → <see cref="ArgumentException"/>.</item>
/// <item><see langword="null"/> <c>visibilityByIndex</c> → <see cref="ArgumentNullException"/>.</item>
/// <item>Битый PDF / IO-сбой → пробрасывается caller'у.</item>
/// <item>Индексы в <c>visibilityByIndex</c>, отсутствующие в документе, игнорируются
/// silently (best-effort, как у <see cref="IPdfFormFillService"/>).</item>
/// <item>PDF без <c>/OCProperties</c> → <see cref="ReadLayersAsync"/> возвращает пустой
/// список; <see cref="SetLayerVisibilityAsync"/> копирует source в target без изменений.</item>
/// </list>
/// </summary>
public interface IPdfOcgService
{
    /// <summary>Возвращает снимок слоёв документа на момент чтения. Порядок соответствует
    /// массиву <c>/OCProperties → /OCGs</c> в catalog'е (стабильный между чтениями
    /// одного и того же файла).</summary>
    Task<IReadOnlyList<PdfLayer>> ReadLayersAsync(string sourcePath, CancellationToken ct);

    /// <summary>Сохраняет PDF в <paramref name="targetPath"/> (atomic temp+Move) с обновлёнными
    /// default-visibility слоёв. Оригинал не мутируется (паттерн watermark/redact). Ключи
    /// словаря — индексы из <see cref="PdfLayer.Index"/>, значения — желаемая видимость.
    /// Отсутствующие в документе индексы игнорируются.</summary>
    Task SetLayerVisibilityAsync(
        string sourcePath,
        string targetPath,
        IReadOnlyDictionary<int, bool> visibilityByIndex,
        CancellationToken ct);
}
