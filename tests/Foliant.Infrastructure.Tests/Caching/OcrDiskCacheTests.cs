using FluentAssertions;
using Foliant.Domain;
using Foliant.Infrastructure.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.Infrastructure.Tests.Caching;

public sealed class OcrDiskCacheTests
{
    private readonly IDiskCache _disk = Substitute.For<IDiskCache>();
    private readonly OcrDiskCache _sut;

    public OcrDiskCacheTests()
    {
        _sut = new OcrDiskCache(_disk, NullLogger<OcrDiskCache>.Instance);
    }

    [Fact]
    public async Task TryGet_NoEntry_ReturnsNull()
    {
        _disk.TryGetAsync(Arg.Any<CacheKey>(), Arg.Any<CancellationToken>()).Returns((byte[]?)null);

        var result = await _sut.TryGetAsync(K(), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task PutThenGet_RoundTripsTextLayer()
    {
        byte[]? captured = null;
        _disk.PutAsync(Arg.Any<CacheKey>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
             .Returns(call =>
             {
                 captured = ((ReadOnlyMemory<byte>)call[1]).ToArray();
                 return Task.CompletedTask;
             });

        var original = new TextLayer(7, [
            new TextRun("Hello", 1, 2, 3, 4),
            new TextRun("Привет", 5.5, 6.6, 7.7, 8.8),
        ]);
        await _sut.PutAsync(K(), original, default);

        captured.Should().NotBeNull();
        _disk.TryGetAsync(Arg.Any<CacheKey>(), Arg.Any<CancellationToken>()).Returns(captured);

        var roundtripped = await _sut.TryGetAsync(K(), default);

        roundtripped.Should().NotBeNull();
        roundtripped!.PageIndex.Should().Be(7);
        roundtripped.Runs.Should().HaveCount(2);
        roundtripped.Runs[0].Text.Should().Be("Hello");
        roundtripped.Runs[1].Text.Should().Be("Привет");
        roundtripped.Runs[1].X.Should().BeApproximately(5.5, 0.0001);
    }

    [Fact]
    public async Task Put_GzipPayload_IsSmallerThanRaw()
    {
        // Highly-compressible repeated text → GZip should make it dramatically smaller.
        byte[]? compressed = null;
        _disk.PutAsync(Arg.Any<CacheKey>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
             .Returns(call =>
             {
                 compressed = ((ReadOnlyMemory<byte>)call[1]).ToArray();
                 return Task.CompletedTask;
             });

        var bigText = new string('a', 10_000);
        var layer = new TextLayer(0, [new TextRun(bigText, 0, 0, 0, 0)]);
        await _sut.PutAsync(K(), layer, default);

        compressed!.Length.Should().BeLessThan(500, "10 КБ повторяющегося текста должны жмуться < 500 байт");
    }

    [Fact]
    public async Task TryGet_CorruptBytes_ReturnsNull_DoesNotThrow()
    {
        _disk.TryGetAsync(Arg.Any<CacheKey>(), Arg.Any<CancellationToken>())
             .Returns([1, 2, 3, 4, 5]);

        var result = await _sut.TryGetAsync(K(), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Put_NullArgs_Throw()
    {
        var act1 = () => _sut.PutAsync(null!, new TextLayer(0, []), default);
        var act2 = () => _sut.PutAsync(K(), null!, default);

        await act1.Should().ThrowAsync<ArgumentNullException>();
        await act2.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task TryGet_NullKey_Throws()
    {
        var act = () => _sut.TryGetAsync(null!, default);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private static CacheKey K() => new("fp", 0, 1, 0, 0x100);
}
