using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Чтение списка link-аннотаций PDF (ISO 32000-1 §12.5.6.5, §12.6.4.2): обход словарей страниц
/// <c>/Annots</c> со сбором аннотаций <c>/Subtype /Link</c> и разрешением их целей (внешний URI /
/// внутренний GoTo-переход). Только чтение, без записи — в отличие от симметричных attachment /
/// page-label сервисов.
///
/// <para>Stateless; оригинал не открывается на запись.</para>
/// </summary>
public interface IPdfLinkService
{
    /// <summary>
    /// Читает все link-аннотации <paramref name="pdfPath"/>. Документ без ссылок (или повреждённый) →
    /// пустой список (best-effort, без throw'а). Результат упорядочен по
    /// <see cref="PdfLinkAnnotation.PageIndex"/>, а в пределах страницы — по порядку обнаружения в
    /// массиве <c>/Annots</c>.
    /// </summary>
    /// <param name="pdfPath">Путь к PDF.</param>
    /// <param name="ct">Отмена.</param>
    /// <returns>Link-аннотации документа (возможно пустой список).</returns>
    /// <exception cref="System.ArgumentException"><paramref name="pdfPath"/> пуст / whitespace.</exception>
    Task<IReadOnlyList<PdfLinkAnnotation>> ListLinksAsync(string pdfPath, CancellationToken ct);
}
