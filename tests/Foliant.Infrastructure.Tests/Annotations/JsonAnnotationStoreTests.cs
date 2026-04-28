using FluentAssertions;
using Foliant.Domain;
using Foliant.Infrastructure.Annotations;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Infrastructure.Tests.Annotations;

public sealed class JsonAnnotationStoreTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private readonly JsonAnnotationStore _sut;
    private const string Fp = "doc-fp-1";

    public JsonAnnotationStoreTests()
    {
        _sut = new JsonAnnotationStore(_tmp.Path, NullLogger<JsonAnnotationStore>.Instance);
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
    public async Task AddThenList_RoundtripsHighlight()
    {
        var hl = Annotation.Highlight(
            pageIndex: 5,
            bounds: new AnnotationRect(10, 20, 100, 12),
            colorHex: "#FFFF00",
            createdAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        await _sut.AddAsync(Fp, hl, default);
        var result = await _sut.ListAsync(Fp, default);

        result.Should().HaveCount(1);
        result[0].Should().BeEquivalentTo(hl);
    }

    [Fact]
    public async Task AddThenList_RoundtripsStickyNote()
    {
        var note = Annotation.StickyNote(
            pageIndex: 0,
            bounds: new AnnotationRect(50, 60, 16, 16),
            text: "TODO: fix this paragraph — Привет!",
            colorHex: "#FFCC00",
            createdAt: DateTimeOffset.UtcNow);

        await _sut.AddAsync(Fp, note, default);
        var result = await _sut.ListAsync(Fp, default);

        result[0].Text.Should().Be("TODO: fix this paragraph — Привет!");
        result[0].Kind.Should().Be(AnnotationKind.StickyNote);
    }

    [Fact]
    public async Task AddThenList_RoundtripsFreehand()
    {
        var ink = Annotation.Freehand(
            pageIndex: 2,
            points: [new AnnotationPoint(0, 0), new AnnotationPoint(10, 10), new AnnotationPoint(20, 5)],
            colorHex: "#FF0000",
            createdAt: DateTimeOffset.UtcNow);

        await _sut.AddAsync(Fp, ink, default);
        var result = await _sut.ListAsync(Fp, default);

        result[0].InkPoints.Should().NotBeNull();
        result[0].InkPoints!.Should().HaveCount(3);
        result[0].InkPoints![1].X.Should().Be(10);
    }

    [Fact]
    public async Task Add_AppendsToExisting()
    {
        var a = Annotation.Highlight(0, new AnnotationRect(0, 0, 10, 10), "#0F0", DateTimeOffset.UtcNow);
        var b = Annotation.Highlight(1, new AnnotationRect(0, 0, 10, 10), "#F00", DateTimeOffset.UtcNow);

        await _sut.AddAsync(Fp, a, default);
        await _sut.AddAsync(Fp, b, default);
        var result = await _sut.ListAsync(Fp, default);

        result.Should().HaveCount(2);
        result.Select(x => x.Id).Should().BeEquivalentTo([a.Id, b.Id]);
    }

    [Fact]
    public async Task Update_ReplacesById()
    {
        var orig = Annotation.StickyNote(0, new AnnotationRect(0, 0, 10, 10), "v1", "#FFF", DateTimeOffset.UtcNow);
        await _sut.AddAsync(Fp, orig, default);

        var edited = orig with { Text = "v2" };
        await _sut.UpdateAsync(Fp, edited, default);

        var result = await _sut.ListAsync(Fp, default);
        result.Should().ContainSingle().Which.Text.Should().Be("v2");
    }

    [Fact]
    public async Task Update_UnknownId_Throws()
    {
        var ghost = Annotation.Highlight(0, new AnnotationRect(0, 0, 10, 10), "#000", DateTimeOffset.UtcNow);

        var act = () => _sut.UpdateAsync(Fp, ghost, default);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Remove_ExistingId_ReturnsTrueAndDrops()
    {
        var a = Annotation.Highlight(0, new AnnotationRect(0, 0, 10, 10), "#000", DateTimeOffset.UtcNow);
        await _sut.AddAsync(Fp, a, default);

        var removed = await _sut.RemoveAsync(Fp, a.Id, default);

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
        var a = Annotation.Highlight(0, new AnnotationRect(0, 0, 10, 10), "#000", DateTimeOffset.UtcNow);
        await _sut.AddAsync(Fp, a, default);

        await _sut.RemoveAllAsync(Fp, default);

        (await _sut.ListAsync(Fp, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Survives_Restart()
    {
        var hl = Annotation.Highlight(0, new AnnotationRect(0, 0, 10, 10), "#ABCDEF", DateTimeOffset.UtcNow);
        await _sut.AddAsync(Fp, hl, default);

        using var fresh = new JsonAnnotationStore(_tmp.Path, NullLogger<JsonAnnotationStore>.Instance);
        var result = await fresh.ListAsync(Fp, default);

        result.Should().ContainSingle().Which.Id.Should().Be(hl.Id);
    }

    [Fact]
    public async Task ConcurrentAdds_AllSurvive()
    {
        var tasks = Enumerable.Range(0, 20).Select(i =>
            _sut.AddAsync(Fp, Annotation.Highlight(
                pageIndex: i,
                bounds: new AnnotationRect(0, 0, 10, 10),
                colorHex: "#000",
                createdAt: DateTimeOffset.UtcNow), default)).ToArray();

        await Task.WhenAll(tasks);

        var result = await _sut.ListAsync(Fp, default);
        result.Should().HaveCount(20);
    }

    [Fact]
    public async Task DifferentDocuments_AreIsolated()
    {
        var a = Annotation.Highlight(0, new AnnotationRect(0, 0, 10, 10), "#000", DateTimeOffset.UtcNow);
        var b = Annotation.Highlight(0, new AnnotationRect(0, 0, 10, 10), "#FFF", DateTimeOffset.UtcNow);

        await _sut.AddAsync("doc-A", a, default);
        await _sut.AddAsync("doc-B", b, default);

        (await _sut.ListAsync("doc-A", default)).Should().ContainSingle().Which.Id.Should().Be(a.Id);
        (await _sut.ListAsync("doc-B", default)).Should().ContainSingle().Which.Id.Should().Be(b.Id);
    }

    [Fact]
    public async Task NullArgs_Throw()
    {
        var act1 = () => _sut.ListAsync(null!, default);
        var act2 = () => _sut.AddAsync(null!, Annotation.Highlight(0, new(0, 0, 10, 10), "#000", DateTimeOffset.UtcNow), default);
        var act3 = () => _sut.AddAsync(Fp, null!, default);

        await act1.Should().ThrowAsync<ArgumentException>();
        await act2.Should().ThrowAsync<ArgumentException>();
        await act3.Should().ThrowAsync<ArgumentNullException>();
    }
}
