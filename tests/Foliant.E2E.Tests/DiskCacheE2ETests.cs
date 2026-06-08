using FluentAssertions;
using Foliant.Domain;
using Foliant.Infrastructure.Caching;
using Xunit;

namespace Foliant.E2E.Tests;

/// <summary>
/// End-to-end render-cache round-trip against the real SQLite disk cache: render a page through the
/// pipeline, store its BGRA32 bytes under a <see cref="CacheKey"/>, read them back byte-identical,
/// then evict — proving the persistence layer the reader relies on between sessions.
/// </summary>
[Trait("Category", "E2E")]
public sealed class DiskCacheE2ETests
{
    [Fact]
    public async Task RenderedPage_RoundTripsThroughTheDiskCache()
    {
        await using var host = new FoliantPipelineHost();
        await using IDocument doc = await host.OpenAsync(E2EFixtures.TextPdf());

        using IPageRender render = await doc.RenderPageAsync(0, new RenderOptions(Zoom: 1.0), CancellationToken.None);
        byte[] pixels = render.Bgra32.ToArray();

        IDiskCache cache = host.Get<IDiskCache>();
        var key = new CacheKey(DocFingerprint: "e2e-fp", PageIndex: 0, EngineVersion: 1, ZoomBucket: 100, Flags: 0);

        await cache.PutAsync(key, pixels, CancellationToken.None);
        byte[]? roundTripped = await cache.TryGetAsync(key, CancellationToken.None);

        roundTripped.Should().NotBeNull();
        roundTripped!.Should().Equal(pixels, "the disk cache must return the exact bytes that were stored");
    }

    [Fact]
    public async Task DiskCache_MissReturnsNull_AndRemoveEvicts()
    {
        await using var host = new FoliantPipelineHost();
        IDiskCache cache = host.Get<IDiskCache>();
        var key = new CacheKey("evict-fp", 3, 1, 100, 0);
        byte[] payload = [1, 2, 3, 4, 5, 6, 7, 8];

        (await cache.TryGetAsync(key, CancellationToken.None)).Should().BeNull("nothing stored yet");

        await cache.PutAsync(key, payload, CancellationToken.None);
        (await cache.TryGetAsync(key, CancellationToken.None)).Should().NotBeNull();

        (await cache.RemoveAsync(key, CancellationToken.None)).Should().BeTrue();
        (await cache.TryGetAsync(key, CancellationToken.None)).Should().BeNull("the entry was evicted");
    }

    [Fact]
    public async Task DiskCache_InvalidateDocument_DropsAllItsPages()
    {
        await using var host = new FoliantPipelineHost();
        IDiskCache cache = host.Get<IDiskCache>();
        const string fp = "multi-page-fp";

        for (int page = 0; page < 5; page++)
        {
            await cache.PutAsync(new CacheKey(fp, page, 1, 100, 0), new byte[] { (byte)page, 9, 9, 9 }, CancellationToken.None);
        }

        int removed = await cache.InvalidateDocumentAsync(fp, CancellationToken.None);

        removed.Should().Be(5);
        (await cache.TryGetAsync(new CacheKey(fp, 2, 1, 100, 0), CancellationToken.None)).Should().BeNull();
    }
}
