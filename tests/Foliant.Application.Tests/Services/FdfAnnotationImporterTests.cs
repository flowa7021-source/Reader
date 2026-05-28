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
        // /Underline — нами не поддерживается; должен быть проигнорирован, валидный Highlight рядом
        // остаётся.
        string fdf = """
            %FDF-1.2
            1 0 obj
            <<
            /FDF
            <<
            /Annots [
            << /Type /Annot /Subtype /Underline /Page 0 /Rect [0 0 10 10] /C [0 0 0] >>
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
}
