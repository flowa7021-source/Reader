using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Xunit;

namespace Foliant.Application.Tests.Services;

public sealed class AnnotationMergeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 27, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EmptyExisting_ReturnsAllIncoming()
    {
        var incoming = new[]
        {
            Annotation.Highlight(0, new AnnotationRect(1, 2, 3, 4), "#FF0000", T0),
            Annotation.StickyNote(1, new AnnotationRect(0, 0, 16, 16), "hi", "#FFCC00", T0),
        };

        AnnotationMerge.NewAnnotations([], incoming).Should().HaveCount(2);
    }

    [Fact]
    public void ContentDuplicate_WithDifferentId_IsSkipped()
    {
        var existing = Annotation.Highlight(0, new AnnotationRect(1, 2, 3, 4), "#FF0000", T0);
        // Same content, fresh Id (as import would produce):
        var reimported = Annotation.Highlight(0, new AnnotationRect(1, 2, 3, 4), "#FF0000", T0);

        existing.Id.Should().NotBe(reimported.Id);
        AnnotationMerge.NewAnnotations([existing], [reimported]).Should().BeEmpty();
    }

    [Fact]
    public void DuplicateIgnoresCreatedAt_ReimportIsIdempotent()
    {
        var existing = Annotation.Highlight(0, new AnnotationRect(1, 2, 3, 4), "#FF0000", T0);
        var laterSameContent = Annotation.Highlight(0, new AnnotationRect(1, 2, 3, 4), "#FF0000", T0.AddHours(5));

        AnnotationMerge.NewAnnotations([existing], [laterSameContent]).Should().BeEmpty();
    }

    [Fact]
    public void ColorComparison_IsCaseInsensitive()
    {
        var existing = Annotation.Highlight(0, new AnnotationRect(1, 2, 3, 4), "#ff0000", T0);
        var incoming = Annotation.Highlight(0, new AnnotationRect(1, 2, 3, 4), "#FF0000", T0);

        AnnotationMerge.NewAnnotations([existing], [incoming]).Should().BeEmpty();
    }

    [Fact]
    public void DifferentPage_OrColor_OrGeometry_AreDistinct()
    {
        var existing = Annotation.Highlight(0, new AnnotationRect(1, 2, 3, 4), "#FF0000", T0);
        var otherPage = Annotation.Highlight(1, new AnnotationRect(1, 2, 3, 4), "#FF0000", T0);
        var otherColor = Annotation.Highlight(0, new AnnotationRect(1, 2, 3, 4), "#00FF00", T0);
        var otherRect = Annotation.Highlight(0, new AnnotationRect(1, 2, 3, 5), "#FF0000", T0);

        AnnotationMerge.NewAnnotations([existing], [otherPage, otherColor, otherRect])
            .Should().HaveCount(3);
    }

    [Fact]
    public void StickyNote_DifferentText_IsDistinct_SameTextIsDuplicate()
    {
        var existing = Annotation.StickyNote(0, new AnnotationRect(0, 0, 16, 16), "alpha", "#FFCC00", T0);
        var sameText = Annotation.StickyNote(0, new AnnotationRect(0, 0, 16, 16), "alpha", "#FFCC00", T0);
        var diffText = Annotation.StickyNote(0, new AnnotationRect(0, 0, 16, 16), "beta", "#FFCC00", T0);

        AnnotationMerge.NewAnnotations([existing], [sameText, diffText])
            .Should().ContainSingle().Which.Text.Should().Be("beta");
    }

    [Fact]
    public void Freehand_DifferentPoints_IsDistinct()
    {
        var existing = Annotation.Freehand(0, [new AnnotationPoint(1, 1), new AnnotationPoint(2, 2)], "#000000", T0);
        var same = Annotation.Freehand(0, [new AnnotationPoint(1, 1), new AnnotationPoint(2, 2)], "#000000", T0);
        var different = Annotation.Freehand(0, [new AnnotationPoint(1, 1), new AnnotationPoint(9, 9)], "#000000", T0);

        AnnotationMerge.NewAnnotations([existing], [same, different]).Should().ContainSingle();
    }

    [Fact]
    public void IncomingInternalDuplicates_CollapseToOne()
    {
        var a = Annotation.Highlight(0, new AnnotationRect(1, 2, 3, 4), "#FF0000", T0);
        var b = Annotation.Highlight(0, new AnnotationRect(1, 2, 3, 4), "#FF0000", T0);

        AnnotationMerge.NewAnnotations([], [a, b]).Should().ContainSingle();
    }

    [Fact]
    public void PreservesIncomingOrder()
    {
        var first = Annotation.Highlight(0, new AnnotationRect(1, 1, 1, 1), "#111111", T0);
        var second = Annotation.Highlight(2, new AnnotationRect(2, 2, 2, 2), "#222222", T0);

        var result = AnnotationMerge.NewAnnotations([], [first, second]);

        result[0].PageIndex.Should().Be(0);
        result[1].PageIndex.Should().Be(2);
    }
}
