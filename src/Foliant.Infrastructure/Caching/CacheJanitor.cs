using System.Diagnostics.CodeAnalysis;
using Foliant.Application.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Foliant.Infrastructure.Caching;

/// <summary>
/// Фоновая задача, держит DiskCache ниже soft-limit (90 % от hard).
/// Тикает раз в <see cref="CacheJanitorOptions.Interval"/>. См. план, раздел 5.1.
/// </summary>
public sealed class CacheJanitor(
    IDiskCache diskCache,
    ISettingsService settings,
    CacheJanitorOptions options,
    ILogger<CacheJanitor> log) : BackgroundService
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Interval > TimeSpan.Zero ? options.Interval : DefaultInterval;
        log.LogInformation(
            "CacheJanitor started: soft={SoftPct}%, tick={Interval}",
            options.SoftLimitPercent, interval);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await TickAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    // Hard-limit берём из живой пользовательской настройки (Settings → Disk cache limit),
    // а не из статичного options: иначе изменение лимита в UI не вступало бы в силу до рестарта.
    // options.HardLimitBytes — фолбэк, если настройка не задана (<= 0).
    private long ResolveHardLimit()
    {
        long configured = settings.Current.Cache.DiskLimitBytes;
        return configured > 0 ? configured : options.HardLimitBytes;
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Background service must not crash on tick failure; exception is logged.")]
    internal async Task TickAsync(CancellationToken ct)
    {
        try
        {
            var hard = ResolveHardLimit();
            var current = diskCache.CurrentSizeBytes;
            var soft = hard * options.SoftLimitPercent / 100;
            if (current <= hard)
            {
                return;
            }

            var evicted = await diskCache.EvictToTargetAsync(soft, ct).ConfigureAwait(false);
            log.LogInformation("CacheJanitor evicted {Evicted} entries (was {Was}, target {Target})",
                evicted, current, soft);
        }
        catch (OperationCanceledException)
        {
            // shutdown — игнорируем.
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "CacheJanitor tick failed");
        }
    }
}

public sealed record CacheJanitorOptions
{
    public long HardLimitBytes { get; init; } = 5L * 1024 * 1024 * 1024;  // 5 ГБ default из плана
    public int SoftLimitPercent { get; init; } = 90;
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(30);
}
