using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Application.Settings;
using Foliant.Domain;
using Foliant.Infrastructure.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.Infrastructure.Tests.Caching;

public sealed class CacheJanitorTests
{
    [Fact]
    public async Task Tick_BelowHardLimit_DoesNothing()
    {
        var disk = Substitute.For<IDiskCache>();
        disk.CurrentSizeBytes.Returns(50L);
        var sut = NewJanitor(disk, liveLimit: 100, softPct: 90);

        await sut.TickAsync(default);

        await disk.DidNotReceive().EvictToTargetAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tick_AboveHardLimit_EvictsToSoft()
    {
        var disk = Substitute.For<IDiskCache>();
        disk.CurrentSizeBytes.Returns(150L);
        disk.EvictToTargetAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(7);
        var sut = NewJanitor(disk, liveLimit: 100, softPct: 90);

        await sut.TickAsync(default);

        await disk.Received(1).EvictToTargetAsync(90, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tick_DiskThrows_DoesNotPropagate()
    {
        var disk = Substitute.For<IDiskCache>();
        disk.CurrentSizeBytes.Returns(150L);
        disk.EvictToTargetAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new InvalidOperationException("boom"));
        var sut = NewJanitor(disk, liveLimit: 100, softPct: 90);

        var act = () => sut.TickAsync(default);

        await act.Should().NotThrowAsync();
    }

    // Живая пользовательская настройка имеет приоритет над статичным options.HardLimitBytes:
    // лимит 100 (из настроек) перекрывает огромный options-фолбэк, поэтому эвикция срабатывает.
    [Fact]
    public async Task Tick_UsesLiveSetting_OverOptions()
    {
        var disk = Substitute.For<IDiskCache>();
        disk.CurrentSizeBytes.Returns(150L);
        disk.EvictToTargetAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(1);
        var sut = NewJanitor(disk, liveLimit: 100, softPct: 90, optionsHardLimit: 1_000_000);

        await sut.TickAsync(default);

        await disk.Received(1).EvictToTargetAsync(90, Arg.Any<CancellationToken>());
    }

    // Если настройка не задана (<= 0), падаем на options.HardLimitBytes.
    [Fact]
    public async Task Tick_FallsBackToOptions_WhenSettingUnset()
    {
        var disk = Substitute.For<IDiskCache>();
        disk.CurrentSizeBytes.Returns(150L);
        disk.EvictToTargetAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(1);
        var sut = NewJanitor(disk, liveLimit: 0, softPct: 90, optionsHardLimit: 100);

        await sut.TickAsync(default);

        await disk.Received(1).EvictToTargetAsync(90, Arg.Any<CancellationToken>());
    }

    private static CacheJanitor NewJanitor(
        IDiskCache disk, long liveLimit, int softPct, long optionsHardLimit = 0)
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(
            AppSettings.Default with { Cache = new CacheSettings { DiskLimitBytes = liveLimit } });

        return new CacheJanitor(
            disk,
            settings,
            new CacheJanitorOptions
            {
                HardLimitBytes = optionsHardLimit > 0 ? optionsHardLimit : liveLimit,
                SoftLimitPercent = softPct,
                Interval = TimeSpan.FromMilliseconds(50),
            },
            NullLogger<CacheJanitor>.Instance);
    }
}
