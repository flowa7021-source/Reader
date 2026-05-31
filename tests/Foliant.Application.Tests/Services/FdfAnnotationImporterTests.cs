using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Xunit;

namespace Foliant.Application.Tests.Services;

public sealed class FdfAnnotationImporterTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    [Fact]
    public void RoundTrip_HighlightNoteInk_PreservesKindPageBoundsColorTextInk()
    {
        var original = new[]
        {
            Annotation.Highlight(0, new AnnotationRect(10, 20, 30, 40), "#FF0000", T0),
            Annotation.StickyNote(1, new AnnotationRect(5, 5, 16, 16), "Привет — заметка", "#FFCC00", T0),
            Annotation.Freehand(
                2,
                [new AnnotationPoint(100, 100), new AnnotationPoint(150, 180), new AnnotationPoint(200, 120)],
                "#0000FF",
                T0),
        };

        string fdf = new FdfAnnotationExporter().Export(original);
        var imported = new FdfAnnotationImporter().Import(fdf);

        imported.Should().HaveCount(3);

        var h = imported[0];
        h.Kind.Should().Be(AnnotationKind.Highlight);
        h.PageIndex.Should().Be(0);
        h.Bounds.Should().Be(new AnnotationRect(10, 20, 30, 40));
        h.ColorHex.Should().Be("#FF0000");

        var n = imported[1];
        n.Kind.Should().Be(AnnotationKind.StickyNote);
        n.PageIndex.Should().Be(1);
        n.Bounds.Should().Be(new AnnotationRect(5, 5, 16, 16));
        n.ColorHex.Should().Be("#FFCC00");
        n.Text.Should().Be("Привет — заметка");

        var ink = imported[2];
        ink.Kind.Should().Be(AnnotationKind.Freehand);
        ink.PageIndex.Should().Be(2);
        ink.ColorHex.Should().Be("#0000FF");
        ink.InkPoints.Should().Equal(
            new AnnotationPoint(100, 100),
            new AnnotationPoint(150, 180),
            new AnnotationPoint(200, 120));
    }

    [Fact]
    public void Import_EmptyAnnotsArray_ReturnsEmpty()
    {
        string fdf = new FdfAnnotationExporter().Export([]);

        new FdfAnnotationImporter().Import(fdf).Should().BeEmpty();
    }

    [Fact]
    public void Import_MissingHeader_Throws()
    {
        var act = () => new FdfAnnotationImporter().Import("not an fdf file");

        act.Should().Throw<FormatException>().WithMessage("*%FDF-*");
    }

    [Fact]
    public void Import_UnknownSubtype_IsSkipped()
    {
        // /Caret — нами не поддерживается; должен быть проигнорирован, валидный Highlight рядом
        // остаётся.
        string fdf = """
            %FDF-1.2
            1 0 obj
            <<
            /FDF
            <<
            /Annots [
            << /Type /Annot /Subtype /Caret /Page 0 /Rect [0 0 10 10] /C [0 0 0] >>
            << /Type /Annot /Subtype /Highlight /Page 0 /Rect [10 20 40 60] /C [1 0 0] /QuadPoints [10 60 40 60 10 20 40 20] >>
            ]
            >>
            >>
            endobj
            trailer
            << /Root 1 0 R >>
            %%EOF
            """;

        var imported = new FdfAnnotationImporter().Import(fdf);

        imported.Should().ContainSingle()
            .Which.Kind.Should().Be(AnnotationKind.Highlight);
    }

    [Fact]
    public void Import_LiteralStringContents_AcrobatStyle_ParsesAscii()
    {
        // Acrobat может писать /Contents литералом, а не hex; должен парситься.
        string fdf = """
            %FDF-1.2
            1 0 obj
            << /FDF << /Annots [
            << /Type /Annot /Subtype /Text /Page 0 /Rect [5 5 21 21] /C [1 0.8 0] /Contents (Hello, note) >>
            ] >> >>
            endobj
            trailer << /Root 1 0 R >>
            %%EOF
            """;

        var imported = new FdfAnnotationImporter().Import(fdf);

        imported.Should().ContainSingle()
            .Which.Text.Should().Be("Hello, note");
    }

    [Fact]
    public void Import_LiteralStringWithUtf16Bom_DecodesCyrillic()
    {
        // Литерал с UTF-16BE BOM, кириллица записана октальными escape'ами:
        // \376\377 = FE FF (BOM), затем UTF-16BE кодпойнты "Привет" = 041F 0440 0438 0432 0435 0442.
        string fdf = """
            %FDF-1.2
            1 0 obj
            << /FDF << /Annots [
            << /Type /Annot /Subtype /Text /Page 0 /Rect [0 0 10 10] /C [0 0 0] /Contents (\376\377\004\037\004\100\004\070\004\062\004\065\004\102) >>
            ] >> >>
            endobj
            trailer << /Root 1 0 R >>
            %%EOF
            """;

        var imported = new FdfAnnotationImporter().Import(fdf);

        imported.Should().ContainSingle()
            .Which.Text.Should().Be("Привет");
    }

    [Fact]
    public void Import_CreationDate_WhenPresent_IsParsedAsUtc()
    {
        string fdf = """
            %FDF-1.2
            1 0 obj
            << /FDF << /Annots [
            << /Type /Annot /Subtype /Highlight /Page 0 /Rect [0 0 10 10] /C [0 0 0]
               /QuadPoints [0 10 10 10 0 0 10 0] /CreationDate (D:20240115103045Z) >>
            ] >> >>
            endobj
            trailer << /Root 1 0 R >>
            %%EOF
            """;

        var a = new FdfAnnotationImporter().Import(fdf).Single();

        a.CreatedAt.UtcDateTime.Should().Be(new DateTime(2024, 1, 15, 10, 30, 45, DateTimeKind.Utc));
    }

    [Fact]
    public void RoundTrip_WithMetadata_PreservesAuthorSubjectAndModifiedAt()
    {
        var modified = new DateTimeOffset(2024, 6, 15, 12, 30, 45, TimeSpan.Zero);
        var original = new[]
        {
            Annotation.Highlight(0, new AnnotationRect(10, 20, 30, 40), "#FF0000", T0) with
            {
                ModifiedAt = modified,
                Author = "Иван Петров",
                Subject = "Замечание",
            },
        };

        string fdf = new FdfAnnotationExporter().Export(original);
        var imported = new FdfAnnotationImporter().Import(fdf);

        var a = imported.Should().ContainSingle().Subject;
        a.Author.Should().Be("Иван Петров");
        a.Subject.Should().Be("Замечание");
        a.ModifiedAt.Should().Be(modified);
        a.CreatedAt.Should().Be(T0);
    }

    [Fact]
    public void Import_WithMissingOptionalMetadata_LeavesFieldsNull()
    {
        // Round-trip без метаданных: ModifiedAt/Author/Subject должны остаться null.
        string fdf = new FdfAnnotationExporter().Export(
            [Annotation.Highlight(0, new AnnotationRect(0, 0, 10, 10), "#000000", T0)]);

        var a = new FdfAnnotationImporter().Import(fdf).Should().ContainSingle().Subject;

        a.ModifiedAt.Should().BeNull();
        a.Author.Should().BeNull();
        a.Subject.Should().BeNull();
    }

    [Fact]
    public void Import_MalformedAnnotationDict_IsSkipped_NotFatal()
    {
        // Highlight без /Rect — пропустить; следом нормальная аннотация попадает.
        string fdf = """
            %FDF-1.2
            1 0 obj
            << /FDF << /Annots [
            << /Type /Annot /Subtype /Highlight /Page 0 /C [0 0 0] >>
            << /Type /Annot /Subtype /Text /Page 1 /Rect [0 0 10 10] /C [0 0 0] /Contents <48693E> >>
            ] >> >>
            endobj
            trailer << /Root 1 0 R >>
            %%EOF
            """;

        var imported = new FdfAnnotationImporter().Import(fdf);

        imported.Should().ContainSingle()
            .Which.Should().Match<Annotation>(a => a.Kind == AnnotationKind.StickyNote && a.Text == "Hi>");
    }

    // ───── Q-F16 shapes / text-markup round-trip ─────

    [Fact]
    public void RoundTrip_AllShapeKinds_PreservesEverything()
    {
        var bounds = new AnnotationRect(10, 20, 30, 40);
        AnnotationPoint[] lineEnds = [new(1, 2), new(50, 60)];
        AnnotationPoint[] poly =
        [
            new(0, 0), new(10, 0), new(10, 10), new(0, 10),
        ];

        var src = new[]
        {
            Annotation.Underline(0, bounds, "#0000FF", T0),
            Annotation.Strikethrough(1, bounds, "#FF0000", T0),
            Annotation.Rectangle(2, bounds, "#00FF00", T0),
            Annotation.Ellipse(3, bounds, "#FF00FF", T0),
            Annotation.Line(4, lineEnds, "#000000", T0),
            Annotation.Arrow(5, lineEnds, "#FF8800", T0),
            Annotation.Polygon(6, poly, "#888888", T0),
        };

        string fdf = new FdfAnnotationExporter().Export(src);
        var imported = new FdfAnnotationImporter().Import(fdf);

        imported.Should().BeEquivalentTo(src, o => o.Excluding(a => a.Id));
    }

    [Fact]
    public void Import_AcrobatStyleSquareAndCircle_MapToRectangleAndEllipse()
    {
        const string fdf = """
            %FDF-1.2
            1 0 obj
            << /FDF << /Annots [
            << /Type /Annot /Subtype /Square /Page 0 /Rect [0 0 40 30] /C [0 1 0] >>
            << /Type /Annot /Subtype /Circle /Page 1 /Rect [5 5 45 55] /C [1 0 1] >>
            ] >> >>
            endobj
            trailer << /Root 1 0 R >>
            %%EOF
            """;

        var imported = new FdfAnnotationImporter().Import(fdf);

        imported.Should().HaveCount(2);
        imported[0].Kind.Should().Be(AnnotationKind.Rectangle);
        imported[0].Bounds.Should().Be(new AnnotationRect(0, 0, 40, 30));
        imported[1].Kind.Should().Be(AnnotationKind.Ellipse);
    }

    [Fact]
    public void Import_LineWithoutLE_IsPlainLine()
    {
        const string fdf = """
            %FDF-1.2
            1 0 obj
            << /FDF << /Annots [
            << /Type /Annot /Subtype /Line /Page 0 /C [0 0 0] /L [0 0 100 50] >>
            ] >> >>
            endobj
            trailer << /Root 1 0 R >>
            %%EOF
            """;

        var a = new FdfAnnotationImporter().Import(fdf).Single();

        a.Kind.Should().Be(AnnotationKind.Line);
        a.InkPoints.Should().Equal(new AnnotationPoint(0, 0), new AnnotationPoint(100, 50));
    }

    [Theory]
    [InlineData("/None /OpenArrow")]
    [InlineData("/OpenArrow /None")]
    [InlineData("/ClosedArrow /ClosedArrow")]
    public void Import_LineWithNonNoneLE_IsArrow(string le)
    {
        string fdf = $$"""
            %FDF-1.2
            1 0 obj
            << /FDF << /Annots [
            << /Type /Annot /Subtype /Line /Page 0 /C [0 0 0] /L [0 0 10 10] /LE [{{le}}] >>
            ] >> >>
            endobj
            trailer << /Root 1 0 R >>
            %%EOF
            """;

        var a = new FdfAnnotationImporter().Import(fdf).Single();

        a.Kind.Should().Be(AnnotationKind.Arrow);
    }

    [Fact]
    public void Import_Polygon_ReadsVertices()
    {
        const string fdf = """
            %FDF-1.2
            1 0 obj
            << /FDF << /Annots [
            << /Type /Annot /Subtype /Polygon /Page 0 /C [0 0 0] /Vertices [0 0 10 0 10 10 0 10] >>
            ] >> >>
            endobj
            trailer << /Root 1 0 R >>
            %%EOF
            """;

        var a = new FdfAnnotationImporter().Import(fdf).Single();

        a.Kind.Should().Be(AnnotationKind.Polygon);
        a.InkPoints.Should().HaveCount(4);
    }

    [Fact]
    public void Import_PolygonWithFewerThanThreeVertices_IsSkipped()
    {
        // /Vertices has 4 values (2 points) → polygon requires ≥3 vertices → skipped silently.
        const string fdf = """
            %FDF-1.2
            1 0 obj
            << /FDF << /Annots [
            << /Type /Annot /Subtype /Polygon /Page 0 /C [0 0 0] /Vertices [0 0 10 10] >>
            ] >> >>
            endobj
            trailer << /Root 1 0 R >>
            %%EOF
            """;

        new FdfAnnotationImporter().Import(fdf).Should().BeEmpty();
    }

    [Fact]
    public void Import_UnderlineAndStrikeout_MapsToCorrectKinds()
    {
        const string fdf = """
            %FDF-1.2
            1 0 obj
            << /FDF << /Annots [
            << /Type /Annot /Subtype /Underline /Page 0 /Rect [0 0 40 5] /C [0 0 1] >>
            << /Type /Annot /Subtype /StrikeOut /Page 1 /Rect [0 0 40 5] /C [1 0 0] >>
            ] >> >>
            endobj
            trailer << /Root 1 0 R >>
            %%EOF
            """;

        var imported = new FdfAnnotationImporter().Import(fdf);

        imported[0].Kind.Should().Be(AnnotationKind.Underline);
        imported[1].Kind.Should().Be(AnnotationKind.Strikethrough);
    }
}
