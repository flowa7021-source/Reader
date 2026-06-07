using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Preflight / проверка структуры PDF: одна read-only-операция, отвечающая «годен ли документ для
/// печати / архива / пересылки?». Реализация <b>композирует</b> уже существующие инспекторы
/// (<see cref="IPdfFontService"/>, <see cref="IPdfSanitizationService"/>,
/// <see cref="IPdfOutputIntentService"/>, <see cref="IPdfLinkService"/>) и добавляет структурную
/// выжимку из PdfPig — собирая всё в один <see cref="PdfPreflightReport"/>. Severity / findings из
/// отчёта выводит UI; сам отчёт — голые факты, без вердиктов. Только чтение, без записи — симметрично
/// остальным listing/scan-сервисам.
///
/// <para>Stateless; оригинал не открывается на запись.</para>
/// </summary>
public interface IPdfPreflightService
{
    /// <summary>
    /// Собирает preflight-отчёт по <paramref name="pdfPath"/>. Best-effort: повреждённый /
    /// отсутствующий / нечитаемый PDF даёт отчёт с безопасными значениями по умолчанию
    /// (<see cref="PdfPreflightReport.PageCount"/> = 0, пустая версия, не зашифрован, пустые
    /// под-инспекторы), а не исключение — единственное исключение — guard на пустой путь.
    /// </summary>
    /// <param name="pdfPath">Путь к PDF.</param>
    /// <param name="ct">Отмена.</param>
    /// <returns>Сводный отчёт о структуре документа (возможно с дефолтными значениями).</returns>
    /// <exception cref="System.ArgumentException"><paramref name="pdfPath"/> пуст / whitespace.</exception>
    Task<PdfPreflightReport> PreflightAsync(string pdfPath, CancellationToken ct);
}
