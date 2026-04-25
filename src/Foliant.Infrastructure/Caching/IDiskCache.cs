using Foliant.Domain;

namespace Foliant.Infrastructure.Caching;

/// <summary>
/// Persistent (disk) cache: bytes per CacheKey + LRU eviction.
/// Слой 4 кэша. См. план, раздел 5.1.
/// </summary>
public interface IDiskCache
{
    long CurrentSizeBytes { get; }

    Task<byte[]?> TryGetAsync(CacheKey key, CancellationToken ct);

    Task PutAsync(CacheKey key, ReadOnlyMemory<byte> bytes, CancellationToken ct);

    Task<bool> RemoveAsync(CacheKey key, CancellationToken ct);

    /// <summary>Удаляет все записи документа с указанным fingerprint.</summary>
    Task<int> InvalidateDocumentAsync(string docFingerprint, CancellationToken ct);

    /// <summary>Эвикция LRU-записей, пока размер не ≤ targetBytes.</summary>
    Task<int> EvictToTargetAsync(long targetBytes, CancellationToken ct);

    Task ClearAsync(CancellationToken ct);
}
