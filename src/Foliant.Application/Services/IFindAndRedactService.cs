using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Опции для <see cref="IFindAndRedactService.RedactMatchesAsync"/>. <see cref="CaseSensitive"/>
/// и <see cref="WholeWord"/> мапятся на <see cref="SearchQuery.MatchCase"/> /
/// <see cref="SearchQuery.MatchWholeWord"/>. <see cref="Regex"/> заставляет интерпретировать
/// <c>query</c> как .NET-регулярку: каждый Match'ей даёт отдельный <see cref="RedactionRegion"/>.
/// При <see cref="Regex"/>=<c>true</c> + <see cref="CaseSensitive"/>=<c>false</c> добавляется
/// <see cref="System.Text.RegularExpressions.RegexOptions.IgnoreCase"/>.
/// </summary>
public sealed record FindAndRedactOptions(
    bool CaseSensitive = false,
    bool WholeWord = false,
    bool Regex = false,
    bool FoldDiacritics = false);

/// <summary>
/// Find-and-redact: ищет вхождения <c>query</c> в документе через <see cref="ISearchService"/>
/// и физически редактирует каждый матч через <see cref="IRedactionService"/>.
///
/// MVP (Q-F32 partial follow-up): bbox = bounding box того <see cref="TextRun"/>, в который
/// попало начало совпадения (PDF text layer пока пер-строчный — sub-character координат нет;
/// если match'и пересекают строки, редактируется только bbox первой строки).
/// </summary>
public interface IFindAndRedactService
{
    /// <summary>
    /// Возвращает количество отредактированных регионов. 0 если нет матчей — в этом случае
    /// <see cref="IRedactionService.RedactAsync"/> НЕ вызывается (никакого output-файла).
    /// </summary>
    /// <exception cref="ArgumentException">Null/empty <paramref name="query"/>.</exception>
    Task<int> RedactMatchesAsync(
        IDocument document,
        string sourcePath,
        string targetPath,
        string query,
        FindAndRedactOptions options,
        CancellationToken ct);
}
