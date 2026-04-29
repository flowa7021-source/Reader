using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.Infrastructure.Annotations;

public sealed class AnnotationService(
    IAnnotationStore store,
    IFileFingerprint fingerprint,
    ILogger<AnnotationService> log) : IAnnotationService
{
    public async Task<IReadOnlyList<Annotation>> ListAsync(string documentPath, CancellationToken ct)
    {
        var fp = await fingerprint.ComputeAsync(documentPath, ct).ConfigureAwait(false);
        return await store.ListAsync(fp, ct).ConfigureAwait(false);
    }

    public async Task AddAsync(string documentPath, Annotation annotation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        var fp = await fingerprint.ComputeAsync(documentPath, ct).ConfigureAwait(false);
        await store.AddAsync(fp, annotation, ct).ConfigureAwait(false);
        log.LogDebug("Added {Kind} annotation {Id} to {Path}", annotation.Kind, annotation.Id, documentPath);
    }

    public async Task UpdateAsync(string documentPath, Annotation annotation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        var fp = await fingerprint.ComputeAsync(documentPath, ct).ConfigureAwait(false);
        await store.UpdateAsync(fp, annotation, ct).ConfigureAwait(false);
    }

    public async Task<bool> RemoveAsync(string documentPath, Guid annotationId, CancellationToken ct)
    {
        var fp = await fingerprint.ComputeAsync(documentPath, ct).ConfigureAwait(false);
        return await store.RemoveAsync(fp, annotationId, ct).ConfigureAwait(false);
    }
}
