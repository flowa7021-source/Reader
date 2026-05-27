using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Xunit;

namespace Foliant.Application.Tests.Services;

public sealed class AnnotationToPdfSpecTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Highlight_MapsRectQuadPointsAndTranslucentColor()
    {
        var hl = Annotation.Highlight(2, new AnnotationRect(10, 20, 30, 40), "#FF0000", T0);

        var spec = AnnotationToPdfSpec.Map(hl)!;

        spec.PageIndex.Should().Be(2);
        spec.Subtype.Should().Be(PdfAnnotationSubtype.Highlight);
        spec.Rect.Should().Be(new PdfRect(10, 20, 40, 60));
        spec.Color.Should().Be(new PdfRgba(255, 0, 0, 102));   // translucent
        spec.QuadPoints.Should().Equal(10, 60, 40, 60, 10, 20, 40, 20);
        spec.Contents.Should().BeNull();
        spec.InkPoints.Should().BeNull();
    }

    [Fact]
    public void StickyNote_MapsContentsAndOpaqueColor()
    {
        var note = Annotation.StickyNote(0, new AnnotationRect(5, 5, 16, 16), "Привет", "#FFCC00", T0);

        var spec = AnnotationToPdfSpec.Map(note)!;

        spec.Subtype.Should().Be(PdfAnnotationSubtype.Text);
        spec.Contents.Should().Be("Привет");
        spec.Color.Should().Be(new PdfRgba(255, 204, 0, 255));   // opaque
        spec.Rect.Should().Be(new PdfRect(5, 5, 21, 21));
    }

    [Fact]
    public void Freehand_MapsInkPointsAndBoundingRect()
    {
        var ink = Annotation.Freehand(
            1,
            [new AnnotationPoint(10, 50), new AnnotationPoint(30, 10), new AnnotationPoint(20, 70)],
            "#000000",
            T0);

        var spec = AnnotationToPdfSpec.Map(ink)!;

        spec.Subtype.Should().Be(PdfAnnotationSubtype.Ink);
        spec.InkPoints.Should().HaveCount(3);
        // Bounding rect = (minX,minY,maxX,maxY) = (10,10,30,70)
        spec.Rect.Should().Be(new PdfRect(10, 10, 30, 70));
        spec.Color.Should().Be(new PdfRgba(0, 0, 0, 255));
    }

    [Fact]
    public void InvalidColor_FallsBackToBlackKeepingAlpha()
    {
        var hl = Annotation.Highlight(0, new AnnotationRect(0, 0, 1, 1), "not-a-color", T0);

        AnnotationToPdfSpec.Map(hl)!.Color.Should().Be(new PdfRgba(0, 0, 0, 102));
    }

    [Fact]
    public void Map_SkipsMalformed_HighlightWithoutBounds_FreehandTooFewPoints()
    {
        var noBounds = new Annotation(Guid.NewGuid(), 0, AnnotationKind.Highlight, "#FFF", null, null, null, T0);
        var onePoint = new Annotation(Guid.NewGuid(), 0, AnnotationKind.Freehand, "#000", null, null, [new AnnotationPoint(1, 1)], T0);

        AnnotationToPdfSpec.Map(noBounds).Should().BeNull();
        AnnotationToPdfSpec.Map(onePoint).Should().BeNull();
    }

    [Fact]
    public void MapMany_DropsInvalid_KeepsValid_PreservingOrder()
    {
        var valid1 = Annotation.Highlight(0, new AnnotationRect(0, 0, 1, 1), "#FFF", T0);
        var invalid = new Annotation(Guid.NewGuid(), 0, AnnotationKind.Highlight, "#FFF", null, null, null, T0);
        var valid2 = Annotation.StickyNote(1, new AnnotationRect(0, 0, 2, 2), "x", "#000", T0);

        var specs = AnnotationToPdfSpec.MapMany([valid1, invalid, valid2]);

        specs.Should().HaveCount(2);
        specs[0].Subtype.Should().Be(PdfAnnotationSubtype.Highlight);
        specs[1].Subtype.Should().Be(PdfAnnotationSubtype.Text);
    }
}
