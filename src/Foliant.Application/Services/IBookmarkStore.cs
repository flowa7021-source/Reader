using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Per-document хранилище закладок (ключ — fingerprint).
/// Phase 1: JSON sidecar в <c>{LocalAppData}/Foliant/Bookmarks/{fp}.json</c>.
/// </summary>
public interface IBookmarkStore
{
    Task<IReadOnlyList<Bookmark>> ListAsync(string docFingerprint, CancellationToken ct);

    Task AddAsync(string docFingerprint, Bookmark bookmark, CancellationToken ct);

    /// <summary>Заменить existing bookmark с тем же Id. Бросает <see cref="KeyNotFoundException"/>
    /// если такой Id отсутствует — clobber-by-id вместо silent-create.</summary>
    Task UpdateAsync(string docFingerprint, Bookmark bookmark, CancellationToken ct);

    Task<bool> RemoveAsync(string docFingerprint, Guid bookmarkId, CancellationToken ct);

    Task RemoveAllAsync(string docFingerprint, CancellationToken ct);
}
