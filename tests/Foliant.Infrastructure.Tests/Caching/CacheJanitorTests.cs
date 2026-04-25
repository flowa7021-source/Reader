using FluentAssertions;
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
        var sut = NewJanitor(disk, hardLimit: 100, softPct: 90);

        await sut.TickAsync(default);

        await disk.DidNotReceive().EvictToTargetAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tick_AboveHardLimit_EvictsToSoft()
    {
        var disk = Substitute.For<IDiskCache>();
        disk.CurrentSizeBytes.Returns(150L);
        disk.EvictToTargetAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(7);
        var sut = NewJanitor(disk, hardLimit: 100, softPct: 90);

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
        var sut = NewJanitor(disk, hardLimit: 100, softPct: 90);

        var act = () => sut.TickAsync(default);

        await act.Should().NotThrowAsync();
    }

    private static CacheJanitor NewJanitor(IDiskCache disk, long hardLimit, int softPct) =>
        new(disk,
            new CacheJanitorOptions
            {
                HardLimitBytes = hardLimit,
                SoftLimitPercent = softPct,
                Interval = TimeSpan.FromMilliseconds(50),
            },
            NullLogger<CacheJanitor>.Instance);
}
