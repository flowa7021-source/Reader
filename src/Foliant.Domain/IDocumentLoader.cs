namespace Foliant.Domain;

/// <summary>
/// Резолвится по filename / magic bytes. Каждый engine регистрирует свою реализацию.
/// </summary>
public interface IDocumentLoader
{
    DocumentKind Kind { get; }

    bool CanLoad(string path);

    Task<IDocument> LoadAsync(string path, CancellationToken ct);
}
