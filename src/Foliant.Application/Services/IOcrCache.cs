using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Persistent кэш OCR-результатов (слой 4). Реализация — поверх
/// <c>IDiskCache</c> с GZip+JSON сериализацией <see cref="TextLayer"/>.
/// </summary>
public interface IOcrCache
{
    Task<TextLayer?> TryGetAsync(CacheKey key, CancellationToken ct);

    Task PutAsync(CacheKey key, TextLayer layer, CancellationToken ct);
}
