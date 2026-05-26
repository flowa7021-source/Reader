using FluentAssertions;
using Xunit;

namespace Foliant.Domain.Tests;

public sealed class BookmarkTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_PopulatesFields_AndGeneratesId()
    {
        var bookmark = Bookmark.Create(pageIndex: 42, "Chapter 3", Now);

        bookmark.PageIndex.Should().Be(42);
        bookmark.Label.Should().Be("Chapter 3");
        bookmark.CreatedAt.Should().Be(Now);
        bookmark.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Create_ProducesUniqueIds_PerCall()
    {
        var a = Bookmark.Create(0, "a", Now);
        var b = Bookmark.Create(0, "a", Now);

        a.Id.Should().NotBe(b.Id);
    }

    [Fact]
    public void Equality_IsValueBased_WhenIdAndFieldsMatch()
    {
        var bookmark = Bookmark.Create(5, "Intro", Now);
        var copy = bookmark with { };

        copy.Should().Be(bookmark);
        copy.GetHashCode().Should().Be(bookmark.GetHashCode());
    }

    [Fact]
    public void With_OverridesTargetedField_Only()
    {
        var bookmark = Bookmark.Create(5, "Intro", Now);

        var moved = bookmark with { PageIndex = 6 };

        moved.PageIndex.Should().Be(6);
        moved.Id.Should().Be(bookmark.Id);
        moved.Label.Should().Be(bookmark.Label);
        moved.CreatedAt.Should().Be(bookmark.CreatedAt);
    }
}
