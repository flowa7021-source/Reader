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

    // ───── Q-F16 shapes ─────

    [Fact]
    public void Rectangle_MapsToSquareWithOpaqueColor()
    {
        var r = Annotation.Rectangle(0, new AnnotationRect(10, 20, 30, 40), "#00FF00", T0);

        var spec = AnnotationToPdfSpec.Map(r)!;

        spec.Subtype.Should().Be(PdfAnnotationSubtype.Square);
        spec.Rect.Should().Be(new PdfRect(10, 20, 40, 60));
        spec.Color.Should().Be(new PdfRgba(0, 255, 0, 255));
    }

    [Fact]
    public void Ellipse_MapsToCircleWithOpaqueColor()
    {
        var e = Annotation.Ellipse(2, new AnnotationRect(0, 0, 100, 50), "#FF00FF", T0);

        var spec = AnnotationToPdfSpec.Map(e)!;

        spec.Subtype.Should().Be(PdfAnnotationSubtype.Circle);
        spec.Color.Should().Be(new PdfRgba(255, 0, 255, 255));
    }

    [Fact]
    public void Line_Arrow_Polygon_AreSkipped_UntilCosLevelSettersAvailable()
    {
        // PDFium 146.x не экспонирует /L /Vertices /LE setter'ы — embedding этих типов
        // отложен до отдельного PR с cos-level fallback'ом. См. #75/#76 для FDF/XFDF
        // round-trip'а (там эти типы работают полностью).
        var line = Annotation.Line(0,
            [new AnnotationPoint(0, 0), new AnnotationPoint(10, 10)], "#000", T0);
        var arrow = Annotation.Arrow(0,
            [new AnnotationPoint(0, 0), new AnnotationPoint(10, 10)], "#000", T0);
        var poly = Annotation.Polygon(0,
            [new AnnotationPoint(0, 0), new AnnotationPoint(10, 0), new AnnotationPoint(5, 10)],
            "#000", T0);

        AnnotationToPdfSpec.Map(line).Should().BeNull();
        AnnotationToPdfSpec.Map(arrow).Should().BeNull();
        AnnotationToPdfSpec.Map(poly).Should().BeNull();
    }

    [Fact]
    public void Shapes_WithoutRequiredData_AreSkipped()
    {
        var noBoundsRect = new Annotation(Guid.NewGuid(), 0, AnnotationKind.Rectangle, "#000", null, null, null, T0);
        var noBoundsEllipse = new Annotation(Guid.NewGuid(), 0, AnnotationKind.Ellipse, "#000", null, null, null, T0);

        AnnotationToPdfSpec.Map(noBoundsRect).Should().BeNull();
        AnnotationToPdfSpec.Map(noBoundsEllipse).Should().BeNull();
    }

    // ───── Q-F18 stamp ─────

    [Fact]
    public void Stamp_MapsToStampSubtypeWithContentsAndOpaqueColor()
    {
        var stamp = Annotation.Stamp(2, new AnnotationRect(100, 100, 200, 60), "APPROVED", "#00AA00", T0);

        var spec = AnnotationToPdfSpec.Map(stamp)!;

        spec.PageIndex.Should().Be(2);
        spec.Subtype.Should().Be(PdfAnnotationSubtype.Stamp);
        spec.Rect.Should().Be(new PdfRect(100, 100, 300, 160));
        spec.Contents.Should().Be("APPROVED");
        spec.Color.Should().Be(new PdfRgba(0, 170, 0, 255));
    }

    [Fact]
    public void Stamp_WithoutBoundsOrLabel_IsSkipped()
    {
        var noBounds = new Annotation(Guid.NewGuid(), 0, AnnotationKind.Stamp, "#000", null, "X", null, T0);
        var blankLabel = new Annotation(Guid.NewGuid(), 0, AnnotationKind.Stamp, "#000", new AnnotationRect(0, 0, 10, 10), "   ", null, T0);

        AnnotationToPdfSpec.Map(noBounds).Should().BeNull();
        AnnotationToPdfSpec.Map(blankLabel).Should().BeNull();
    }

    [Fact]
    public void Stamp_PreservesMetadata()
    {
        var modified = new DateTimeOffset(2024, 6, 15, 0, 0, 0, TimeSpan.Zero);
        var stamp = Annotation.Stamp(0, new AnnotationRect(0, 0, 50, 20), "DRAFT", "#FF0000", T0) with
        {
            ModifiedAt = modified,
            Author = "Иван",
        };

        var spec = AnnotationToPdfSpec.Map(stamp)!;

        spec.CreatedAt.Should().Be(T0);
        spec.ModifiedAt.Should().Be(modified);
        spec.Author.Should().Be("Иван");
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

    // ───── Q-F16 underline + strikethrough text-markup ─────

    [Fact]
    public void Underline_MapsRectQuadPointsAndOpaqueColor()
    {
        var u = Annotation.Underline(3, new AnnotationRect(10, 20, 30, 40), "#0000FF", T0);

        var spec = AnnotationToPdfSpec.Map(u)!;

        spec.PageIndex.Should().Be(3);
        spec.Subtype.Should().Be(PdfAnnotationSubtype.Underline);
        spec.Rect.Should().Be(new PdfRect(10, 20, 40, 60));
        spec.Color.Should().Be(new PdfRgba(0, 0, 255, 255));   // opaque — line на тексте
        spec.QuadPoints.Should().Equal(10, 60, 40, 60, 10, 20, 40, 20);
    }

    [Fact]
    public void Strikethrough_MapsToStrikeoutSubtypeWithOpaqueColor()
    {
        var s = Annotation.Strikethrough(0, new AnnotationRect(0, 0, 100, 10), "#FF0000", T0);

        var spec = AnnotationToPdfSpec.Map(s)!;

        spec.Subtype.Should().Be(PdfAnnotationSubtype.Strikeout);
        spec.Color.Should().Be(new PdfRgba(255, 0, 0, 255));
        spec.QuadPoints.Should().HaveCount(8);
    }

    [Fact]
    public void TextMarkup_WithoutBounds_IsSkipped()
    {
        var u = new Annotation(Guid.NewGuid(), 0, AnnotationKind.Underline, "#000", null, null, null, T0);
        var s = new Annotation(Guid.NewGuid(), 0, AnnotationKind.Strikethrough, "#000", null, null, null, T0);

        AnnotationToPdfSpec.Map(u).Should().BeNull();
        AnnotationToPdfSpec.Map(s).Should().BeNull();
    }

    [Fact]
    public void TextMarkup_PreservesMetadata()
    {
        var modified = new DateTimeOffset(2024, 6, 15, 0, 0, 0, TimeSpan.Zero);
        var u = Annotation.Underline(0, new AnnotationRect(0, 0, 10, 10), "#000", T0) with
        {
            ModifiedAt = modified,
            Author = "Иван",
            Subject = "Замечание",
        };

        var spec = AnnotationToPdfSpec.Map(u)!;

        spec.CreatedAt.Should().Be(T0);
        spec.ModifiedAt.Should().Be(modified);
        spec.Author.Should().Be("Иван");
        spec.Subject.Should().Be("Замечание");
    }

    // ───── Q-F18 A5b: image-stamp ImagePath plumbing ─────

    [Fact]
    public void ImageStamp_PassesImagePathThroughToPdfSpec()
    {
        var stamp = Annotation.ImageStamp(
            0, new AnnotationRect(10, 20, 200, 60), "/tmp/logo.png", "APPROVED", "#00AA00", T0);

        var spec = AnnotationToPdfSpec.Map(stamp)!;

        spec.Subtype.Should().Be(PdfAnnotationSubtype.Stamp);
        spec.Contents.Should().Be("APPROVED");
        spec.ImagePath.Should().Be("/tmp/logo.png");
    }

    [Fact]
    public void TextStamp_LeavesImagePathNullOnSpec()
    {
        var stamp = Annotation.Stamp(0, new AnnotationRect(0, 0, 100, 30), "DRAFT", "#FF0000", T0);

        AnnotationToPdfSpec.Map(stamp)!.ImagePath.Should().BeNull();
    }
}
