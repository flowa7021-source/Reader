using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Фасад для работы с аннотациями: принимает абсолютный путь документа,
/// внутри получает <see cref="IFileFingerprint"/>-ключ и обращается к
/// <see cref="IAnnotationStore"/>. ViewModel не должна знать про fingerprint.
/// </summary>
public interface IAnnotationService
{
    Task<IReadOnlyList<Annotation>> ListAsync(string documentPath, CancellationToken ct);

    Task AddAsync(string documentPath, Annotation annotation, CancellationToken ct);

    Task UpdateAsync(string documentPath, Annotation annotation, CancellationToken ct);

    Task<bool> RemoveAsync(string documentPath, Guid annotationId, CancellationToken ct);
}
