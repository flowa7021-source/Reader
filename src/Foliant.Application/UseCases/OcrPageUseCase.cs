using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.Application.UseCases;

/// <summary>
/// Распознаёт страницу через <see cref="IOcrEngine"/>, кэшируя результат
/// в <see cref="IOcrCache"/> (слой 4 — диск, GZip+JSON).
/// Кэш-ключ помечен флагом <see cref="OcrFlag"/>, чтобы не пересекаться
/// с render-ключами того же документа/страницы.
/// </summary>
public sealed class OcrPageUseCase(
    IOcrEngine engine,
    IOcrCache cache,
    ILogger<OcrPageUseCase> log)
{
    /// <summary>Бит в <see cref="CacheKey.Flags"/>, отделяющий OCR-записи от render-записей.</summary>
    public const int OcrFlag = 0x100;

    public async Task<TextLayer> ExecuteAsync(
        IPageRender render,
        string docFingerprint,
        int pageIndex,
        OcrOptions options,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(docFingerprint);
        ArgumentNullException.ThrowIfNull(options);

        var key = new CacheKey(
            DocFingerprint: docFingerprint,
            PageIndex: pageIndex,
            EngineVersion: engine.Version,
            ZoomBucket: 0,
            Flags: OcrFlag);

        var cached = await cache.TryGetAsync(key, ct).ConfigureAwait(false);
        if (cached is not null)
        {
            log.LogDebug("OCR cache hit for {Fp}/{Page}", docFingerprint, pageIndex);
            return cached;
        }

        var result = await engine.RecognizeAsync(render, pageIndex, options, ct).ConfigureAwait(false);
        await cache.PutAsync(key, result, ct).ConfigureAwait(false);
        log.LogInformation("OCR completed for {Fp}/{Page}: {Runs} runs", docFingerprint, pageIndex, result.Runs.Count);
        return result;
    }
}
