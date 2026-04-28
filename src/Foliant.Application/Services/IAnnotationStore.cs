using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Хранилище аннотаций. Per-document — ключ <c>docFingerprint</c>.
/// Phase 1 / S10/A: реализация — JSON sidecar (`%LOCALAPPDATA%/Foliant/Annotations/{fp}.json`).
/// Phase 2: переезд на embedding в сам PDF через PdfPig.
/// </summary>
public interface IAnnotationStore
{
    Task<IReadOnlyList<Annotation>> ListAsync(string docFingerprint, CancellationToken ct);

    Task AddAsync(string docFingerprint, Annotation annotation, CancellationToken ct);

    Task UpdateAsync(string docFingerprint, Annotation annotation, CancellationToken ct);

    Task<bool> RemoveAsync(string docFingerprint, Guid annotationId, CancellationToken ct);

    Task RemoveAllAsync(string docFingerprint, CancellationToken ct);
}
