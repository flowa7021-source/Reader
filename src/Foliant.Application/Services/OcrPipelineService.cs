using Foliant.Application.UseCases;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.Application.Services;

/// <summary>
/// Прогресс-снимок для <see cref="OcrPipelineService.RecognizeDocumentAsync"/>.
/// </summary>
public readonly record struct OcrProgress(int CompletedPages, int TotalPages);

/// <summary>
/// Оркестратор OCR для всего документа: обходит страницы по порядку,
/// вызывает <see cref="OcrPageUseCase"/> (кэш + движок) для каждой,
/// сообщает прогресс через <see cref="IProgress{T}"/>.
///
/// Поведение при ошибке: страница пропускается (возвращается
/// <see cref="TextLayer.Empty"/>), ошибка логируется. Это позволяет
/// хотя бы распознать оставшиеся страницы вместо падения всего батча.
/// </summary>
public sealed class OcrPipelineService(
    OcrPageUseCase pageUseCase,
    ILogger<OcrPipelineService> log,
    ITextLayerCache? textCache = null)
{
    /// <summary>
    /// Распознаёт все страницы <paramref name="document"/>, возвращает список
    /// <see cref="TextLayer"/> в порядке страниц (индекс 0 = страница 0).
    /// </summary>
    /// <param name="document">Документ, чьи страницы надо OCR-ить.</param>
    /// <param name="docFingerprint">Fingerprint для cache-ключей.</param>
    /// <param name="options">Параметры движка (lang, DPI и т.п.).</param>
    /// <param name="progress">Необязательный получатель прогресса. Каждый вызов после
    /// обработки очередной страницы сообщает (completedPages, totalPages).</param>
    /// <param name="ct">Токен отмены. При отмене частично собранный результат не
    /// возвращается — выбрасывается <see cref="OperationCanceledException"/>.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Per-page OCR failure must not abort the entire batch; we log and substitute Empty.")]
    public async Task<IReadOnlyList<TextLayer>> RecognizeDocumentAsync(
        IDocument document,
        string docFingerprint,
        OcrOptions options,
        IProgress<OcrProgress>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(docFingerprint);
        ArgumentNullException.ThrowIfNull(options);

        int total = document.PageCount;
        var results = new TextLayer[total];

        for (int i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();

            // L2 in-memory cache hit: skip render + engine entirely.
            if (textCache is not null && textCache.TryGet(i, out TextLayer cached))
            {
                log.LogDebug("OcrPipeline: in-memory cache hit for {Fp} page {Page}.", docFingerprint, i);
                results[i] = cached;
                progress?.Report(new OcrProgress(i + 1, total));
                continue;
            }

            IPageRender render;
            try
            {
                render = await document.RenderPageAsync(i, new RenderOptions(Zoom: 1.0), ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "OcrPipeline: render failed for {Fp} page {Page}; substituting empty layer.", docFingerprint, i);
                results[i] = TextLayer.Empty(i);
                progress?.Report(new OcrProgress(i + 1, total));
                continue;
            }

            try
            {
                var layer = await pageUseCase.ExecuteAsync(render, docFingerprint, i, options, ct)
                    .ConfigureAwait(false);
                results[i] = layer;
                textCache?.Put(i, layer);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "OcrPipeline: OCR failed for {Fp} page {Page}; substituting empty layer.", docFingerprint, i);
                results[i] = TextLayer.Empty(i);
            }
            finally
            {
                render.Dispose();
            }

            progress?.Report(new OcrProgress(i + 1, total));
        }

        return results;
    }
}
