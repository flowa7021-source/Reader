using FluentAssertions;
using Foliant.Domain;
using Foliant.Infrastructure.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Infrastructure.Tests.Caching;

[Trait("Category", "Integration")]
public sealed class SqliteDiskCacheTests : IAsyncLifetime
{
    private readonly TempDir _tmp = new();
    private SqliteDiskCache _sut = null!;

    public Task InitializeAsync()
    {
        _sut = new SqliteDiskCache(_tmp.Path, NullLogger<SqliteDiskCache>.Instance);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _sut.DisposeAsync();
        _tmp.Dispose();
    }

    [Fact]
    public async Task PutThenGet_RoundTrips()
    {
        var key = K(0);
        var data = RandomBytes(128);

        await _sut.PutAsync(key, data, default);
        var got = await _sut.TryGetAsync(key, default);

        got.Should().BeEquivalentTo(data);
    }

    [Fact]
    public async Task TryGet_Missing_ReturnsNull()
    {
        var got = await _sut.TryGetAsync(K(99), default);

        got.Should().BeNull();
    }

    [Fact]
    public async Task Put_OverwritesValueAtSameKey()
    {
        var key = K(0);
        await _sut.PutAsync(key, new byte[] { 1, 2, 3 }, default);
        await _sut.PutAsync(key, new byte[] { 4, 5, 6, 7 }, default);

        var got = await _sut.TryGetAsync(key, default);

        got.Should().BeEquivalentTo(new byte[] { 4, 5, 6, 7 });
        _sut.CurrentSizeBytes.Should().Be(4);
    }

    [Fact]
    public async Task CurrentSizeBytes_ReflectsSumOfEntries()
    {
        await _sut.PutAsync(K(0), new byte[100], default);
        await _sut.PutAsync(K(1), new byte[200], default);
        await _sut.PutAsync(K(2), new byte[300], default);

        _sut.CurrentSizeBytes.Should().Be(600);
    }

    [Fact]
    public async Task Remove_DropsEntryAndFile()
    {
        var key = K(0);
        await _sut.PutAsync(key, new byte[100], default);

        var existed = await _sut.RemoveAsync(key, default);

        existed.Should().BeTrue();
        (await _sut.TryGetAsync(key, default)).Should().BeNull();
        _sut.CurrentSizeBytes.Should().Be(0);
    }

    [Fact]
    public async Task InvalidateDocument_RemovesAllPagesOfThatDoc_KeepsOthers()
    {
        await _sut.PutAsync(K(0, fp: "doc-A"), new byte[10], default);
        await _sut.PutAsync(K(1, fp: "doc-A"), new byte[10], default);
        await _sut.PutAsync(K(0, fp: "doc-B"), new byte[10], default);

        var removed = await _sut.InvalidateDocumentAsync("doc-A", default);

        removed.Should().Be(2);
        (await _sut.TryGetAsync(K(0, fp: "doc-A"), default)).Should().BeNull();
        (await _sut.TryGetAsync(K(0, fp: "doc-B"), default)).Should().NotBeNull();
        _sut.CurrentSizeBytes.Should().Be(10);
    }

    [Fact]
    public async Task EvictToTarget_DropsOldestEntries_UntilUnderTarget()
    {
        // Put 5 страниц по 100 байт = 500. Target 250 → должны выгнать 3 самых старых.
        for (var i = 0; i < 5; i++)
        {
            await _sut.PutAsync(K(i), new byte[100], default);
            // микро-задержка не нужна — last_access берёт UtcNow.Ticks (резолюция ≈ 100 нс).
            await Task.Delay(2, default);
        }

        var evicted = await _sut.EvictToTargetAsync(targetBytes: 250, default);

        evicted.Should().BeInRange(3, 5);
        _sut.CurrentSizeBytes.Should().BeLessThanOrEqualTo(250);
        (await _sut.TryGetAsync(K(0), default)).Should().BeNull();   // самая старая
    }

    [Fact]
    public async Task TryGet_PromotesEntry_ProtectingFromEviction()
    {
        for (var i = 0; i < 3; i++)
        {
            await _sut.PutAsync(K(i), new byte[100], default);
            await Task.Delay(2);
        }

        // Поднимаем стр. 0 — она самая «свежая» теперь.
        await Task.Delay(2);
        _ = await _sut.TryGetAsync(K(0), default);

        await _sut.EvictToTargetAsync(targetBytes: 100, default);

        (await _sut.TryGetAsync(K(0), default)).Should().NotBeNull();
        (await _sut.TryGetAsync(K(1), default)).Should().BeNull();
    }

    [Fact]
    public async Task ClearAsync_DropsAllEntriesAndFiles()
    {
        await _sut.PutAsync(K(0), new byte[10], default);
        await _sut.PutAsync(K(1), new byte[10], default);

        await _sut.ClearAsync(default);

        _sut.CurrentSizeBytes.Should().Be(0);
        (await _sut.TryGetAsync(K(0), default)).Should().BeNull();
        Directory.GetFiles(Path.Combine(_tmp.Path, "pages")).Should().BeEmpty();
    }

    [Fact]
    public async Task SurvivesProcessRestart_DataPersists()
    {
        await _sut.PutAsync(K(0), new byte[] { 1, 2, 3 }, default);
        await _sut.PutAsync(K(1), new byte[] { 4, 5, 6 }, default);
        await _sut.DisposeAsync();

        var second = new SqliteDiskCache(_tmp.Path, NullLogger<SqliteDiskCache>.Instance);
        try
        {
            (await second.TryGetAsync(K(0), default)).Should().BeEquivalentTo(new byte[] { 1, 2, 3 });
            (await second.TryGetAsync(K(1), default)).Should().BeEquivalentTo(new byte[] { 4, 5, 6 });
            second.CurrentSizeBytes.Should().Be(6);
        }
        finally
        {
            await second.DisposeAsync();
        }

        // Восстанавливаем _sut, чтобы DisposeAsync в конце не упал.
        _sut = new SqliteDiskCache(_tmp.Path, NullLogger<SqliteDiskCache>.Instance);
    }

    [Fact]
    public async Task ConcurrentPut_NoCorruption()
    {
        var tasks = Enumerable.Range(0, 50).Select(i =>
            _sut.PutAsync(K(i), RandomBytes(64), default));

        await Task.WhenAll(tasks);

        _sut.CurrentSizeBytes.Should().Be(50 * 64);
        for (var i = 0; i < 50; i++)
        {
            (await _sut.TryGetAsync(K(i), default)).Should().NotBeNull();
        }
    }

    private static CacheKey K(int pageIndex, string fp = "doc-fp") =>
        new(fp, pageIndex, EngineVersion: 1, ZoomBucket: 100, Flags: 0);

    private static byte[] RandomBytes(int n)
    {
        var b = new byte[n];
        Random.Shared.NextBytes(b);
        return b;
    }
}
