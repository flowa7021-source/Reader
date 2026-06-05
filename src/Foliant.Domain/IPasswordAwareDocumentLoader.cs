namespace Foliant.Domain;

/// <summary>
/// Опциональный «второй» контракт для loader'ов, умеющих принять пароль (сейчас только PDF).
/// Сознательно отделён от <see cref="IDocumentLoader"/>, чтобы не ломать существующие
/// реализации (EPUB/FB2/MOBI/Image/DjVu), которым пароль не нужен. Use-case делает
/// <c>if (loader is IPasswordAwareDocumentLoader pw)</c> и пробрасывает пароль только в них.
/// </summary>
public interface IPasswordAwareDocumentLoader
{
    Task<IDocument> LoadAsync(string path, string? password, CancellationToken ct);
}
