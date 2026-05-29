using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Запускает набор <see cref="BatchJob"/> над уже-инжектированными per-operation сервисами
/// (<c>IWatermarkService</c>, <c>IHeaderFooterService</c>, <c>IPdfCropService</c>, …).
/// Каждый job обрабатывается изолированно — исключение в одном не отменяет соседей,
/// итог отражается в <see cref="BatchJobResult"/>.
///
/// Контракт:
/// <list type="bullet">
/// <item>Параллелизм управляется параметром <c>maxParallelism</c>; ≤ 0 → fallback к 1
///   (sequential). PDFium-native не потокобезопасен между разными документами, но per-сервис
///   <c>NativeGate</c> сериализует это автоматически, поэтому caller может смело ставить
///   <c>Environment.ProcessorCount</c>.</item>
/// <item><see cref="CancellationToken"/> прерывает запуск НОВЫХ job'ов; уже-стартовавшие
///   докатываются (или сами прерываются, если их сервис уважает токен).</item>
/// <item><c>progress</c> вызывается с каждого worker'а — реализация должна быть thread-safe
///   (UI-марширование на dispatcher — забота VM).</item>
/// </list>
/// </summary>
public interface IBatchJobRunner
{
    Task<IReadOnlyList<BatchJobResult>> RunAsync(
        IReadOnlyList<BatchJob> jobs,
        int maxParallelism,
        IProgress<BatchProgress>? progress,
        CancellationToken ct);
}
