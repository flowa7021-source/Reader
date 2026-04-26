using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// In-document substring search. Итерирует страницы, дёргает <see cref="IDocument.GetTextLayerAsync"/>,
/// собирает <see cref="SearchHit"/>'ы со снипетами. Stateless — кэш text-слоёв
/// (если потребуется) — забота вызывающего кода (DocumentTabViewModel).
///
/// Phase 1: case-insensitive ordinal substring. Без word-boundary, без regex,
/// без accent-folding (S6 acceptance — только наличие слова + снипет).
/// </summary>
public interface ISearchService
{
    Task<IReadOnlyList<SearchHit>> SearchInDocumentAsync(
        IDocument document,
        string documentPath,
        SearchQuery query,
        CancellationToken ct);
}
