using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Чтение списка output-intent'ов PDF — массива <c>/OutputIntents</c> каталога (ISO 32000-1 §14.11.5):
/// декларации целевых условий цветопередачи на выводе (PDF/X, PDF/A ICC-профили). В Acrobat —
/// print-production / PDF-X readiness. Только чтение, без записи — симметрично fonts / links listing
/// сервисам.
///
/// <para>Stateless; оригинал не открывается на запись.</para>
/// </summary>
public interface IPdfOutputIntentService
{
    /// <summary>
    /// Читает output-intent'ы из <paramref name="pdfPath"/> в порядке их следования в массиве
    /// <c>/OutputIntents</c>. Документ без <c>/OutputIntents</c> (или повреждённый) → пустой список
    /// (best-effort, без throw'а). Не-словарные элементы массива пропускаются.
    /// </summary>
    /// <param name="pdfPath">Путь к PDF.</param>
    /// <param name="ct">Отмена.</param>
    /// <returns>Output-intent'ы документа в порядке массива (возможно пустой список).</returns>
    /// <exception cref="System.ArgumentException"><paramref name="pdfPath"/> пуст / whitespace.</exception>
    Task<IReadOnlyList<PdfOutputIntent>> ListAsync(string pdfPath, CancellationToken ct);
}
