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

    public async Task<IDocument> ExecuteAsync(string path, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Документ не найден", path);
        }

        var loader = _loaders.FirstOrDefault(l => l.CanLoad(path))
            ?? throw new UnsupportedDocumentException(path);

        log.LogInformation(
            "Открываю {Path} через {Loader} ({Kind})",
            path, loader.GetType().Name, loader.Kind);

        return await loader.LoadAsync(path, ct).ConfigureAwait(false);
    }
}

public sealed class UnsupportedDocumentException(string path)
    : InvalidOperationException($"Не найден loader для документа: {path}")
{
    public string Path { get; } = path;
}
