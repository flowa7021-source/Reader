namespace Foliant.Domain;

/// <summary>
/// Бросается loader'ом, когда документ зашифрован и пароль не задан / не подошёл.
/// VM ловит это, спрашивает пароль у пользователя и повторяет открытие. Наследуется от
/// <see cref="InvalidOperationException"/> по образцу <c>UnsupportedDocumentException</c> —
/// так существующие <c>catch (InvalidOperationException)</c> в headless-пути продолжают
/// показывать сообщение, если UI-промпт не зарегистрирован.
/// </summary>
public sealed class DocumentPasswordRequiredException : InvalidOperationException
{
    public DocumentPasswordRequiredException()
    {
    }

    public DocumentPasswordRequiredException(string message)
        : base(message)
    {
    }

    public DocumentPasswordRequiredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public string? Path { get; private init; }

    public static DocumentPasswordRequiredException ForPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return new($"Документ защищён паролем: {path}") { Path = path };
    }
}
