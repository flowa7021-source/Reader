using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Xunit;

namespace Foliant.Application.Tests.Services;

public sealed class BookmarkMergeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 27, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EmptyExisting_ReturnsAllIncoming()
    {
        var incoming = new[] { Bookmark.Create(0, "A", T0), Bookmark.Create(1, "B", T0) };

        BookmarkMerge.NewBookmarks([], incoming).Should().HaveCount(2);
    }

    [Fact]
    public void ContentDuplicate_WithDifferentId_IsSkipped()
    {
        var existing = Bookmark.Create(3, "Chapter 4", T0);
        var reimported = Bookmark.Create(3, "Chapter 4", T0);

        existing.Id.Should().NotBe(reimported.Id);
        BookmarkMerge.NewBookmarks([existing], [reimported]).Should().BeEmpty();
    }

    [Fact]
    public void DuplicateIgnoresCreatedAt_ReimportIsIdempotent()
    {
        var existing = Bookmark.Create(3, "Chapter 4", T0);
        var laterSame = Bookmark.Create(3, "Chapter 4", T0.AddHours(5));

        BookmarkMerge.NewBookmarks([existing], [laterSame]).Should().BeEmpty();
    }

    [Fact]
    public void SamePage_DifferentLabel_IsDistinct()
    {
        var existing = Bookmark.Create(3, "Chapter 4", T0);
        var sameLabel = Bookmark.Create(3, "Chapter 4", T0);
        var diffLabel = Bookmark.Create(3, "Appendix", T0);

        BookmarkMerge.NewBookmarks([existing], [sameLabel, diffLabel])
            .Should().ContainSingle().Which.Label.Should().Be("Appendix");
    }

    [Fact]
    public void LabelComparison_IsCaseSensitive()
    {
        var existing = Bookmark.Create(0, "Intro", T0);
        var differentCase = Bookmark.Create(0, "intro", T0);

        BookmarkMerge.NewBookmarks([existing], [differentCase]).Should().ContainSingle();
    }

    [Fact]
    public void IncomingInternalDuplicates_CollapseToOne()
    {
        var a = Bookmark.Create(2, "Same", T0);
        var b = Bookmark.Create(2, "Same", T0);

        BookmarkMerge.NewBookmarks([], [a, b]).Should().ContainSingle();
    }

    [Fact]
    public void PreservesIncomingOrder()
    {
        var first = Bookmark.Create(0, "First", T0);
        var second = Bookmark.Create(2, "Second", T0);

        var result = BookmarkMerge.NewBookmarks([], [first, second]);

        result[0].Label.Should().Be("First");
        result[1].Label.Should().Be("Second");
    }
}
