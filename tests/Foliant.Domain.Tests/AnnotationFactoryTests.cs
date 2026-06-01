using FluentAssertions;
using Xunit;

namespace Foliant.Domain.Tests;

public sealed class AnnotationFactoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly AnnotationRect Rect = new(10, 20, 100, 30);

    [Fact]
    public void Highlight_SetsKindAndBounds_AndLeavesTextAndInkNull()
    {
        var a = Annotation.Highlight(pageIndex: 3, Rect, "#FFFF00", Now);

        a.Kind.Should().Be(AnnotationKind.Highlight);
        a.PageIndex.Should().Be(3);
        a.ColorHex.Should().Be("#FFFF00");
        a.Bounds.Should().Be(Rect);
        a.Text.Should().BeNull();
        a.InkPoints.Should().BeNull();
        a.CreatedAt.Should().Be(Now);
        a.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void StickyNote_SetsKindBoundsAndText_AndLeavesInkNull()
    {
        var a = Annotation.StickyNote(pageIndex: 1, Rect, "remember this", "#00FF00", Now);

        a.Kind.Should().Be(AnnotationKind.StickyNote);
        a.PageIndex.Should().Be(1);
        a.ColorHex.Should().Be("#00FF00");
        a.Bounds.Should().Be(Rect);
        a.Text.Should().Be("remember this");
        a.InkPoints.Should().BeNull();
        a.CreatedAt.Should().Be(Now);
        a.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Freehand_SetsKindAndInk_AndLeavesBoundsAndTextNull()
    {
        var points = new[] { new AnnotationPoint(0, 0), new AnnotationPoint(5, 5) };

        var a = Annotation.Freehand(pageIndex: 7, points, "#0000FF", Now);

        a.Kind.Should().Be(AnnotationKind.Freehand);
        a.PageIndex.Should().Be(7);
        a.ColorHex.Should().Be("#0000FF");
        a.InkPoints.Should().BeEquivalentTo(points, o => o.WithStrictOrdering());
        a.Bounds.Should().BeNull();
        a.Text.Should().BeNull();
        a.CreatedAt.Should().Be(Now);
        a.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Factories_ProduceUniqueIds()
    {
        var ids = new[]
        {
            Annotation.Highlight(0, Rect, "#FFFFFF", Now).Id,
            Annotation.Highlight(0, Rect, "#FFFFFF", Now).Id,
            Annotation.StickyNote(0, Rect, "n", "#FFFFFF", Now).Id,
            Annotation.StickyNote(0, Rect, "n", "#FFFFFF", Now).Id,
            Annotation.Freehand(0, new[] { new AnnotationPoint(1, 1) }, "#FFFFFF", Now).Id,
            Annotation.Freehand(0, new[] { new AnnotationPoint(1, 1) }, "#FFFFFF", Now).Id,
        };

        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void AnnotationRect_EqualityIsValueBased()
    {
        new AnnotationRect(1, 2, 3, 4).Should().Be(new AnnotationRect(1, 2, 3, 4));
        new AnnotationRect(1, 2, 3, 4).Should().NotBe(new AnnotationRect(1, 2, 3, 5));
    }

    [Fact]
    public void AnnotationPoint_EqualityIsValueBased()
    {
        new AnnotationPoint(1, 2).Should().Be(new AnnotationPoint(1, 2));
        new AnnotationPoint(1, 2).Should().NotBe(new AnnotationPoint(2, 1));
    }

    // ───── Q-F18 A5: image-stamp factory ─────

    [Fact]
    public void ImageStamp_SetsImagePathAndPreservesLabelAsAccessibilityFallback()
    {
        var when = DateTimeOffset.UnixEpoch;
        var bounds = new AnnotationRect(100, 100, 200, 60);

        var stamp = Annotation.ImageStamp(2, bounds, "/tmp/logo.png", "APPROVED", "#00AA00", when);

        stamp.Kind.Should().Be(AnnotationKind.Stamp);
        stamp.PageIndex.Should().Be(2);
        stamp.Bounds.Should().Be(bounds);
        stamp.Text.Should().Be("APPROVED");
        stamp.ColorHex.Should().Be("#00AA00");
        stamp.ImagePath.Should().Be("/tmp/logo.png");
    }

    [Fact]
    public void Stamp_TextOnly_LeavesImagePathNull()
    {
        var stamp = Annotation.Stamp(0, new AnnotationRect(0, 0, 100, 30), "DRAFT", "#FF0000", DateTimeOffset.UnixEpoch);

        stamp.ImagePath.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ImageStamp_NullOrWhitespaceImagePath_Throws(string? imagePath)
    {
        var act = () => Annotation.ImageStamp(
            0, new AnnotationRect(0, 0, 100, 30), imagePath!, "LABEL", "#000000", DateTimeOffset.UnixEpoch);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ImageStamp_NullOrWhitespaceLabel_Throws()
    {
        var act = () => Annotation.ImageStamp(
            0, new AnnotationRect(0, 0, 100, 30), "/tmp/logo.png", "  ", "#000000", DateTimeOffset.UnixEpoch);

        act.Should().Throw<ArgumentException>();
    }
}
