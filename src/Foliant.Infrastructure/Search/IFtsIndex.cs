using Foliant.Domain;

namespace Foliant.Infrastructure.Search;

public interface IFtsIndex
{
    Task IndexDocumentAsync(
        string docFingerprint,
        string path,
        IAsyncEnumerable<TextLayer> pages,
        CancellationToken ct);

    Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, CancellationToken ct);

    Task<bool> RemoveDocumentAsync(string docFingerprint, CancellationToken ct);

    Task<IReadOnlyList<IndexedDocument>> ListAsync(CancellationToken ct);
}

public sealed record IndexedDocument(
    string Fingerprint,
    string Path,
    int PageCount,
    DateTimeOffset LastIndexed);
