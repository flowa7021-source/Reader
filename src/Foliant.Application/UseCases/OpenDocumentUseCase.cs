using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.Application.UseCases;

/// <summary>
/// Открывает документ, выбирая loader из зарегистрированных по факту CanLoad(path).
/// Никакой логики загрузки здесь нет — только маршрутизация. Это позволяет добавлять
/// новые форматы (DjVu, EPUB) через MEF без правки use case.
/// </summary>
public sealed class OpenDocumentUseCase(
    IEnumerable<IDocumentLoader> loaders,
    ILogger<OpenDocumentUseCase> log)
{
    private readonly IDocumentLoader[] _loaders = [.. loaders];

    // Перегрузка без пароля — сохраняет старую сигнатуру для существующих вызовов
    // (DocumentTabViewModel.Editing.cs reopen после правки структуры): пароль там не нужен.
    public Task<IDocument> ExecuteAsync(string path, CancellationToken ct) =>
        ExecuteAsync(path, password: null, ct);

    public async Task<IDocument> ExecuteAsync(string path, string? password, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Документ не найден", path);
        }

        var loader = _loaders.FirstOrDefault(l => l.CanLoad(path))
            ?? throw UnsupportedDocumentException.ForPath(path);

        log.LogInformation(
            "Открываю {Path} через {Loader} ({Kind})",
            path, loader.GetType().Name, loader.Kind);

        // Пароль пробрасываем только в password-aware loader'ы (PDF). Остальные форматы
        // (EPUB/FB2/MOBI/Image/DjVu) идут по обычному контракту и пароль игнорируют.
        // DocumentPasswordRequiredException здесь НЕ ловим — это работа VM (промпт + retry).
        if (loader is IPasswordAwareDocumentLoader passwordAware)
        {
            return await passwordAware.LoadAsync(path, password, ct).ConfigureAwait(false);
        }

        return await loader.LoadAsync(path, ct).ConfigureAwait(false);
    }
}

public sealed class UnsupportedDocumentException : InvalidOperationException
{
    public UnsupportedDocumentException()
    {
    }

    public UnsupportedDocumentException(string message)
        : base(message)
    {
    }

    public UnsupportedDocumentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public string? Path { get; private init; }

    public static UnsupportedDocumentException ForPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return new($"Не найден loader для документа: {path}") { Path = path };
    }
}
