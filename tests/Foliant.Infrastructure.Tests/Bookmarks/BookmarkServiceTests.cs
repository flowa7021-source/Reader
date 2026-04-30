using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Foliant.Infrastructure.Bookmarks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.Infrastructure.Tests.Bookmarks;

public sealed class BookmarkServiceTests
{
    private const string Path = "/docs/sample.pdf";
    private const string Fp = "fp-resolved-xyz";

    private readonly IBookmarkStore _store = Substitute.For<IBookmarkStore>();
    private readonly IFileFingerprint _fingerprint = Substitute.For<IFileFingerprint>();
    private readonly BookmarkService _sut;

    public BookmarkServiceTests()
    {
        _fingerprint.ComputeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Fp);
        _sut = new BookmarkService(_store, _fingerprint, NullLogger<BookmarkService>.Instance);
    }

    [Fact]
    public async Task List_ResolvesFingerprintAndDelegates()
    {
        var bm = Bookmark.Create(0, "x", DateTimeOffset.UtcNow);
        _store.ListAsync(Fp, Arg.Any<CancellationToken>()).Returns(new[] { bm });

        var result = await _sut.ListAsync(Path, default);

        result.Should().ContainSingle();
        await _fingerprint.Received(1).ComputeAsync(Path, Arg.Any<CancellationToken>());
        await _store.Received(1).ListAsync(Fp, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Add_DelegatesAndReturnsCreatedBookmark()
    {
        var result = await _sut.AddAsync(Path, 7, "Глава 3", default);

        result.PageIndex.Should().Be(7);
        result.Label.Should().Be("Глава 3");
        await _store.Received(1).AddAsync(Fp, Arg.Is<Bookmark>(b =>
            b.Id == result.Id && b.PageIndex == 7 && b.Label == "Глава 3"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Remove_ResolvesFingerprintAndReturnsStoreResult()
    {
        var id = Guid.NewGuid();
        _store.RemoveAsync(Fp, id, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.RemoveAsync(Path, id, default);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Toggle_NoExistingOnPage_AddsAndReturnsBookmark()
    {
        _store.ListAsync(Fp, Arg.Any<CancellationToken>()).Returns(Array.Empty<Bookmark>());

        var result = await _sut.ToggleAsync(Path, 3, "Page 4", default);

        result.Should().NotBeNull();
        result!.PageIndex.Should().Be(3);
        await _store.Received(1).AddAsync(Fp, Arg.Any<Bookmark>(), Arg.Any<CancellationToken>());
        await _store.DidNotReceive().RemoveAsync(Fp, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Toggle_BookmarkExistsOnPage_RemovesAndReturnsNull()
    {
        var existing = Bookmark.Create(3, "stale", DateTimeOffset.UtcNow);
        _store.ListAsync(Fp, Arg.Any<CancellationToken>()).Returns(new[] { existing });

        var result = await _sut.ToggleAsync(Path, 3, "ignored-label", default);

        result.Should().BeNull();
        await _store.Received(1).RemoveAsync(Fp, existing.Id, Arg.Any<CancellationToken>());
        await _store.DidNotReceive().AddAsync(Fp, Arg.Any<Bookmark>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Toggle_ExistingOnDifferentPage_AddsNew()
    {
        var existingOnPage5 = Bookmark.Create(5, "page-5", DateTimeOffset.UtcNow);
        _store.ListAsync(Fp, Arg.Any<CancellationToken>()).Returns(new[] { existingOnPage5 });

        var result = await _sut.ToggleAsync(Path, 7, "page-7", default);

        result.Should().NotBeNull();
        result!.PageIndex.Should().Be(7);
        await _store.Received(1).AddAsync(Fp, Arg.Any<Bookmark>(), Arg.Any<CancellationToken>());
    }
}
