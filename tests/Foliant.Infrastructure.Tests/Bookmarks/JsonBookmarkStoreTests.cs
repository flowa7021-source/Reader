using FluentAssertions;
using Foliant.Domain;
using Foliant.Infrastructure.Bookmarks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Infrastructure.Tests.Bookmarks;

public sealed class JsonBookmarkStoreTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private readonly JsonBookmarkStore _sut;
    private const string Fp = "doc-fp-1";

    public JsonBookmarkStoreTests()
    {
        _sut = new JsonBookmarkStore(_tmp.Path, NullLogger<JsonBookmarkStore>.Instance);
    }

    public void Dispose()
    {
        _sut.Dispose();
        _tmp.Dispose();
    }

    [Fact]
    public async Task List_NoFile_ReturnsEmpty()
    {
        var result = await _sut.ListAsync(Fp, default);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task AddThenList_Roundtrip()
    {
        var bm = Bookmark.Create(5, "Глава 3", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        await _sut.AddAsync(Fp, bm, default);
        var result = await _sut.ListAsync(Fp, default);

        result.Should().ContainSingle().Which.Should().BeEquivalentTo(bm);
    }

    [Fact]
    public async Task Add_AppendsToExisting_AcrossPages()
    {
        var a = Bookmark.Create(0, "Cover", DateTimeOffset.UtcNow);
        var b = Bookmark.Create(7, "Глава 3 — Привет!", DateTimeOffset.UtcNow);

        await _sut.AddAsync(Fp, a, default);
        await _sut.AddAsync(Fp, b, default);

        var result = await _sut.ListAsync(Fp, default);
        result.Select(x => x.Id).Should().BeEquivalentTo([a.Id, b.Id]);
    }

    [Fact]
    public async Task Remove_ExistingId_ReturnsTrueAndDrops()
    {
        var bm = Bookmark.Create(0, "x", DateTimeOffset.UtcNow);
        await _sut.AddAsync(Fp, bm, default);

        var removed = await _sut.RemoveAsync(Fp, bm.Id, default);

        removed.Should().BeTrue();
        (await _sut.ListAsync(Fp, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Remove_UnknownId_ReturnsFalse()
    {
        var removed = await _sut.RemoveAsync(Fp, Guid.NewGuid(), default);
        removed.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveAll_DropsSidecar()
    {
        await _sut.AddAsync(Fp, Bookmark.Create(0, "x", DateTimeOffset.UtcNow), default);

        await _sut.RemoveAllAsync(Fp, default);

        (await _sut.ListAsync(Fp, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Survives_Restart()
    {
        var bm = Bookmark.Create(3, "kept", DateTimeOffset.UtcNow);
        await _sut.AddAsync(Fp, bm, default);

        using var fresh = new JsonBookmarkStore(_tmp.Path, NullLogger<JsonBookmarkStore>.Instance);
        var result = await fresh.ListAsync(Fp, default);

        result.Should().ContainSingle().Which.Id.Should().Be(bm.Id);
    }

    [Fact]
    public async Task ConcurrentAdds_AllSurvive()
    {
        var tasks = Enumerable.Range(0, 20).Select(i =>
            _sut.AddAsync(Fp, Bookmark.Create(i, $"label-{i}", DateTimeOffset.UtcNow), default)).ToArray();

        await Task.WhenAll(tasks);

        var result = await _sut.ListAsync(Fp, default);
        result.Should().HaveCount(20);
    }

    [Fact]
    public async Task DifferentDocuments_AreIsolated()
    {
        var a = Bookmark.Create(0, "A", DateTimeOffset.UtcNow);
        var b = Bookmark.Create(0, "B", DateTimeOffset.UtcNow);

        await _sut.AddAsync("doc-A", a, default);
        await _sut.AddAsync("doc-B", b, default);

        (await _sut.ListAsync("doc-A", default)).Should().ContainSingle().Which.Id.Should().Be(a.Id);
        (await _sut.ListAsync("doc-B", default)).Should().ContainSingle().Which.Id.Should().Be(b.Id);
    }

    [Fact]
    public async Task Update_ExistingId_ReplacesBookmark()
    {
        var bm = Bookmark.Create(3, "old", DateTimeOffset.UtcNow);
        await _sut.AddAsync(Fp, bm, default);

        await _sut.UpdateAsync(Fp, bm with { Label = "new" }, default);

        var result = await _sut.ListAsync(Fp, default);
        result.Should().ContainSingle().Which.Label.Should().Be("new");
    }

    [Fact]
    public async Task Update_UnknownId_ThrowsKeyNotFound()
    {
        var phantom = Bookmark.Create(0, "x", DateTimeOffset.UtcNow);

        var act = () => _sut.UpdateAsync(Fp, phantom, default);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
