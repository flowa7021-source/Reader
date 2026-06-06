namespace Foliant.Domain;

/// <summary>
/// Сводка о document-level скриптах и автоматических действиях PDF (ISO 32000-1 §12.6 / §12.7),
/// которые «висят» на catalog'е и могут выполняться без явного действия пользователя: открытие
/// документа (<c>/OpenAction</c>), document-level JavaScript (<c>/Names → /JavaScript</c>) и
/// document additional-actions (<c>/AA</c>: <c>/WillClose</c>, <c>/WillSave</c>, …). Результат
/// сканирования через <c>IPdfSanitizationService.ScanAsync</c>; на его основе UI решает, предлагать
/// ли «очистку» документа.
///
/// <para>Pure-data, immutable. Per-page <c>/AA</c> и field-level <c>/AA</c> (ISO 32000-1 §12.6.3) в
/// этот отчёт <b>не</b> входят — учитываются только действия уровня catalog'а. <c>with</c>-копии идут
/// мимо какой-либо валидации — это допустимо, как и у других domain-record'ов.</para>
/// </summary>
/// <param name="DocumentJavaScriptNames">Имена записей document-level JavaScript из name-tree
/// <c>/Names → /JavaScript</c> (ключи дерева). Пустой список, если такого дерева нет.</param>
/// <param name="HasJavaScriptOpenAction"><see langword="true"/>, если <c>/OpenAction</c> catalog'а —
/// именно JavaScript-действие (<c>/S /JavaScript</c>); безопасный GoTo / destination-array
/// open-action не считается скриптом и даёт <see langword="false"/>.</param>
/// <param name="HasDocumentAdditionalActions"><see langword="true"/>, если у catalog'а присутствует
/// словарь document additional-actions (<c>/AA</c>).</param>
public sealed record PdfSanitizationReport(
    IReadOnlyList<string> DocumentJavaScriptNames,
    bool HasJavaScriptOpenAction,
    bool HasDocumentAdditionalActions)
{
    /// <summary>
    /// <see langword="true"/>, если документ несёт хоть что-то из document-level скриптов / действий:
    /// непустой <see cref="DocumentJavaScriptNames"/>, JavaScript-<c>/OpenAction</c>
    /// (<see cref="HasJavaScriptOpenAction"/>) или catalog <c>/AA</c>
    /// (<see cref="HasDocumentAdditionalActions"/>). UI использует это как признак «есть что чистить».
    /// </summary>
    public bool HasAnyJavaScriptOrActions =>
        DocumentJavaScriptNames.Count > 0 || HasJavaScriptOpenAction || HasDocumentAdditionalActions;
}
