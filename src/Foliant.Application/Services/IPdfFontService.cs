using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Чтение списка шрифтов, используемых в PDF («Acrobat Document Properties → Fonts», ISO 32000-1
/// §9.5–§9.6): обход словарей страниц <c>/Resources → /Font</c> со сбором уникальных шрифтов. Только
/// чтение, без записи — в отличие от симметричных attachment / page-label сервисов.
///
/// <para>Stateless; оригинал не открывается на запись.</para>
/// </summary>
public interface IPdfFontService
{
    /// <summary>
    /// Читает уникальные шрифты, используемые в <paramref name="pdfPath"/>. Документ без шрифтов (или
    /// повреждённый) → пустой список (best-effort, без throw'а). Дубликаты (один и тот же шрифт на
    /// нескольких страницах) схлопываются. Результат отсортирован по <see cref="PdfFontInfo.Name"/>
    /// (ordinal).
    /// </summary>
    /// <param name="pdfPath">Путь к PDF.</param>
    /// <param name="ct">Отмена.</param>
    /// <returns>Шрифты документа (возможно пустой список).</returns>
    /// <exception cref="System.ArgumentException"><paramref name="pdfPath"/> пуст / whitespace.</exception>
    Task<IReadOnlyList<PdfFontInfo>> ListFontsAsync(string pdfPath, CancellationToken ct);
}
