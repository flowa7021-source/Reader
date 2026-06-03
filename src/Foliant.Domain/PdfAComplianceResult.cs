namespace Foliant.Domain;

/// <summary>
/// Результат валидации PDF против выбранного PDF/A-профиля (ISO 19005). Используется
/// аудиторами и архивными процессами для подтверждения соответствия документа стандарту
/// до подписи / архивации.
///
/// <para><see cref="Profile"/> — машинная метка профиля (например, <c>"PDF/A-1B"</c>), в той
/// же форме, что и выдаёт нижележащий валидатор (veraPDF и т.п.) — она же кладётся в отчёт
/// для аудиторской трассировки.</para>
///
/// <para><see cref="IsCompliant"/> — true тогда и только тогда, когда <see cref="Issues"/>
/// пуст; контракт обязателен для валидатора и проверяется в тестах, чтобы caller'у не
/// приходилось пересчитывать.</para>
///
/// <para><see cref="Issues"/> — упорядоченный список нарушений (по странице, затем по
/// порядку появления). Реализациям рекомендуется не агрегировать «одинаковые» нарушения с
/// разных страниц в одно: юристам нужна полная локализация для отчёта.</para>
/// </summary>
public sealed record PdfAComplianceResult(
    string Profile,
    bool IsCompliant,
    IReadOnlyList<PdfAValidationIssue> Issues);

/// <summary>
/// Одно нарушение PDF/A-профиля: идентификатор правила (например, <c>"6.7.3-2"</c> —
/// clause+rule в нотации veraPDF), человекочитаемое сообщение и опциональный 0-based
/// индекс страницы (null для document-level нарушений: XMP-метаданные, OutputIntent, и т.п.).
/// </summary>
public sealed record PdfAValidationIssue(
    string RuleId,
    string Message,
    int? PageIndex);
