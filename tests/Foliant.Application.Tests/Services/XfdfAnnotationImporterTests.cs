using System.Xml;
using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Xunit;

namespace Foliant.Application.Tests.Services;

public sealed class XfdfAnnotationImporterTests
{
    private readonly XfdfAnnotationImporter _sut = new();

    [Fact]
    public void Roundtrip_ExportThenImport_PreservesAnnotations()
    {
        var when = new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero); // whole seconds: survives PDF-date precision
        var source = new List<Annotation>
        {
            Annotation.Highlight(0, new AnnotationRect(10, 20, 30, 40), "#FFEB3B", when),
            Annotation.StickyNote(2, new AnnotationRect(5, 5, 16, 16), "TODO — Привет!", "#FFCC00", when),
            Annotation.Freehand(1, [new AnnotationPoint(1, 2), new AnnotationPoint(3, 4)], "#000000", when),
        };

        var xfdf = new XfdfAnnotationExporter().Export(source);
        var imported = _sut.Import(xfdf);

        imported.Should().HaveCount(3);
        imported.Should().BeEquivalentTo(
            source,
            o => o.Excluding(a => a.Id)); // Id is regenerated — XFDF carries no identity
    }

    [Fact]
    public void Import_AcrobatStyleHighlight_ParsesRectAndPage()
    {
        const string xfdf = """
            <?xml version="1.0" encoding="UTF-8"?>
            <xfdf xmlns="http://ns.adobe.com/xfdf/">
              <annots>
                <highlight page="3" color="#00FF00" rect="100,200,140,220" />
              </annots>
            </xfdf>
            """;

        var a = _sut.Import(xfdf).Should().ContainSingle().Subject;

        a.Kind.Should().Be(AnnotationKind.Highlight);
        a.PageIndex.Should().Be(3);
        a.ColorHex.Should().Be("#00FF00");
        a.Bounds.Should().Be(new AnnotationRect(100, 200, 40, 20));
    }

    [Fact]
    public void Import_StickyNote_ReadsContents()
    {
        const string xfdf = """
            <xfdf xmlns="http://ns.adobe.com/xfdf/"><annots>
              <text page="0" color="#FFCC00" rect="0,0,16,16"><contents>hello</contents></text>
            </annots></xfdf>
            """;

        var a = _sut.Import(xfdf).Single();

        a.Kind.Should().Be(AnnotationKind.StickyNote);
        a.Text.Should().Be("hello");
    }

    [Fact]
    public void Import_Ink_ParsesGesturePoints()
    {
        const string xfdf = """
            <xfdf xmlns="http://ns.adobe.com/xfdf/"><annots>
              <ink page="0" color="#000000"><inklist><gesture>1,2;3,4;5,6</gesture></inklist></ink>
            </annots></xfdf>
            """;

        var a = _sut.Import(xfdf).Single();

        a.Kind.Should().Be(AnnotationKind.Freehand);
        a.InkPoints.Should().Equal(new AnnotationPoint(1, 2), new AnnotationPoint(3, 4), new AnnotationPoint(5, 6));
    }

    [Fact]
    public void Import_SkipsMalformedElements_KeepsValidOnes()
    {
        const string xfdf = """
            <xfdf xmlns="http://ns.adobe.com/xfdf/"><annots>
              <highlight page="0" rect="1,2,3" />            <!-- bad rect (3 values) → skipped -->
              <text rect="0,0,1,1"><contents>x</contents></text>  <!-- no page → skipped -->
              <highlight page="1" color="#FFF" rect="0,0,10,10" />  <!-- valid -->
            </annots></xfdf>
            """;

        var imported = _sut.Import(xfdf);

        imported.Should().ContainSingle();
        imported[0].PageIndex.Should().Be(1);
    }

    [Fact]
    public void Import_EmptyAnnots_ReturnsEmpty()
    {
        _sut.Import("<xfdf xmlns=\"http://ns.adobe.com/xfdf/\"><annots /></xfdf>").Should().BeEmpty();
    }

    [Fact]
    public void Import_MalformedXml_Throws()
    {
        var act = () => _sut.Import("<xfdf><annots>");

        act.Should().Throw<XmlException>();
    }

    [Fact]
    public void FormatNameAndExtension_AreReasonable()
    {
        _sut.FormatName.Should().Be("XFDF");
        _sut.FileExtension.Should().Be("xfdf");
    }

    [Fact]
    public void Roundtrip_WithMetadata_PreservesAuthorSubjectModifiedAt()
    {
        var created = new DateTimeOffset(2024, 1, 15, 10, 30, 45, TimeSpan.Zero);
        var modified = new DateTimeOffset(2024, 6, 20, 16, 0, 0, TimeSpan.Zero);
        var source = new[]
        {
            Annotation.Highlight(0, new AnnotationRect(10, 20, 30, 40), "#FFEB3B", created) with
            {
                ModifiedAt = modified,
                Author = "Иван Петров",
                Subject = "Замечание",
            },
        };

        var imported = _sut.Import(new XfdfAnnotationExporter().Export(source));

        var a = imported.Should().ContainSingle().Subject;
        a.Author.Should().Be("Иван Петров");
        a.Subject.Should().Be("Замечание");
        a.ModifiedAt.Should().Be(modified);
        a.CreatedAt.Should().Be(created);
    }

    // ───── Q-F16 shapes / text-markup round-trip ─────

    [Fact]
    public void Roundtrip_AllAnnotationKinds_PreservesShapesAndMetadata()
    {
        var when = new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero);
        var bounds = new AnnotationRect(10, 20, 30, 40);
        AnnotationPoint[] lineEnds = [new AnnotationPoint(1, 2), new AnnotationPoint(50, 60)];
        AnnotationPoint[] polyVerts =
        [
            new AnnotationPoint(0, 0), new AnnotationPoint(10, 0),
            new AnnotationPoint(10, 10), new AnnotationPoint(0, 10),
        ];

        var source = new List<Annotation>
        {
            Annotation.Underline(0, bounds, "#0000FF", when),
            Annotation.Strikethrough(1, bounds, "#FF0000", when),
            Annotation.Rectangle(2, bounds, "#00FF00", when),
            Annotation.Ellipse(3, bounds, "#FF00FF", when),
            Annotation.Line(4, lineEnds, "#000000", when),
            Annotation.Arrow(5, lineEnds, "#FF8800", when),
            Annotation.Polygon(6, polyVerts, "#888888", when),
        };

        var xfdf = new XfdfAnnotationExporter().Export(source);
        var imported = _sut.Import(xfdf);

        imported.Should().HaveCount(7);
        imported.Should().BeEquivalentTo(source, o => o.Excluding(a => a.Id));
    }

    [Fact]
    public void Import_AcrobatStyleSquareAndCircle_MapsToRectangleAndEllipse()
    {
        const string xfdf = """
            <xfdf xmlns="http://ns.adobe.com/xfdf/"><annots>
              <square page="0" color="#00FF00" rect="0,0,40,30" />
              <circle page="1" color="#FF00FF" rect="5,5,45,55" />
            </annots></xfdf>
            """;

        var imported = _sut.Import(xfdf);

        imported.Should().HaveCount(2);
        imported[0].Kind.Should().Be(AnnotationKind.Rectangle);
        imported[0].Bounds.Should().Be(new AnnotationRect(0, 0, 40, 30));
        imported[1].Kind.Should().Be(AnnotationKind.Ellipse);
        imported[1].PageIndex.Should().Be(1);
    }

    [Fact]
    public void Import_LineWithoutEndings_IsPlainLine()
    {
        const string xfdf = """
            <xfdf xmlns="http://ns.adobe.com/xfdf/"><annots>
              <line page="0" color="#000000" start="0,0" end="100,50" />
            </annots></xfdf>
            """;

        var a = _sut.Import(xfdf).Single();

        a.Kind.Should().Be(AnnotationKind.Line);
        a.InkPoints.Should().Equal(new AnnotationPoint(0, 0), new AnnotationPoint(100, 50));
    }

    [Theory]
    [InlineData("None", "OpenArrow")]
    [InlineData("OpenArrow", "None")]
    [InlineData("ClosedArrow", "ClosedArrow")]
    public void Import_LineWithNonNoneEnding_IsArrow(string head, string tail)
    {
        string xfdf = $"""
            <xfdf xmlns="http://ns.adobe.com/xfdf/"><annots>
              <line page="0" color="#000000" start="0,0" end="10,10" head="{head}" tail="{tail}" />
            </annots></xfdf>
            """;

        var a = _sut.Import(xfdf).Single();

        a.Kind.Should().Be(AnnotationKind.Arrow);
    }

    [Fact]
    public void Import_PolygonWithSemicolonVertices_ParsesAllPoints()
    {
        const string xfdf = """
            <xfdf xmlns="http://ns.adobe.com/xfdf/"><annots>
              <polygon page="0" color="#000000"><vertices>0,0;10,0;10,10;0,10</vertices></polygon>
            </annots></xfdf>
            """;

        var a = _sut.Import(xfdf).Single();

        a.Kind.Should().Be(AnnotationKind.Polygon);
        a.InkPoints.Should().HaveCount(4);
    }

    [Fact]
    public void Import_PolygonWithFewerThanThreeVertices_IsSkipped()
    {
        const string xfdf = """
            <xfdf xmlns="http://ns.adobe.com/xfdf/"><annots>
              <polygon page="0" color="#000000"><vertices>0,0;10,10</vertices></polygon>
            </annots></xfdf>
            """;

        _sut.Import(xfdf).Should().BeEmpty();
    }

    [Fact]
    public void Import_UnderlineAndStrikeout_MapToCorrectKinds()
    {
        const string xfdf = """
            <xfdf xmlns="http://ns.adobe.com/xfdf/"><annots>
              <underline page="0" color="#0000FF" rect="0,0,40,5" />
              <strikeout page="1" color="#FF0000" rect="0,0,40,5" />
            </annots></xfdf>
            """;

        var imported = _sut.Import(xfdf);

        imported[0].Kind.Should().Be(AnnotationKind.Underline);
        imported[1].Kind.Should().Be(AnnotationKind.Strikethrough);
    }

    [Fact]
    public void Export_AllShapeKinds_ProducesNonNullElements()
    {
        // White-box assertion that exporter doesn't silently drop any of the 11 kinds.
        var when = DateTimeOffset.UnixEpoch;
        var b = new AnnotationRect(0, 0, 10, 10);
        AnnotationPoint[] line = [new(0, 0), new(10, 10)];
        AnnotationPoint[] poly = [new(0, 0), new(10, 0), new(10, 10)];
        var src = new List<Annotation>
        {
            Annotation.Highlight(0, b, "#FFEB3B", when),
            Annotation.Underline(0, b, "#FFEB3B", when),
            Annotation.Strikethrough(0, b, "#FFEB3B", when),
            Annotation.StickyNote(0, b, "x", "#FFEB3B", when),
            Annotation.Freehand(0, line, "#000", when),
            Annotation.Rectangle(0, b, "#000", when),
            Annotation.Ellipse(0, b, "#000", when),
            Annotation.Line(0, line, "#000", when),
            Annotation.Arrow(0, line, "#000", when),
            Annotation.Polygon(0, poly, "#000", when),
            Annotation.Stamp(0, b, "APPROVED", "#00AA00", when),
        };

        string xfdf = new XfdfAnnotationExporter().Export(src);

        var imported = _sut.Import(xfdf);
        imported.Should().HaveCount(11);
    }

    // ───── Q-F18 stamp round-trip ─────

    [Fact]
    public void RoundTrip_Stamp_PreservesLabelBoundsColor()
    {
        var when = new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero);
        var src = new[]
        {
            Annotation.Stamp(2, new AnnotationRect(100, 100, 200, 60), "APPROVED", "#00AA00", when),
        };

        var xfdf = new XfdfAnnotationExporter().Export(src);
        var a = _sut.Import(xfdf).Should().ContainSingle().Subject;

        a.Kind.Should().Be(AnnotationKind.Stamp);
        a.PageIndex.Should().Be(2);
        a.Bounds.Should().Be(new AnnotationRect(100, 100, 200, 60));
        a.Text.Should().Be("APPROVED");
        a.ColorHex.Should().Be("#00AA00");
        a.ImagePath.Should().BeNull();
    }

    [Fact]
    public void RoundTrip_ImageStamp_PreservesImagePathAndLabel()
    {
        var when = new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero);
        var src = new[]
        {
            Annotation.ImageStamp(1, new AnnotationRect(50, 50, 150, 40), "C:\\logos\\seal.png", "APPROVED", "#00AA00", when),
        };

        var xfdf = new XfdfAnnotationExporter().Export(src);
        var a = _sut.Import(xfdf).Should().ContainSingle().Subject;

        a.Kind.Should().Be(AnnotationKind.Stamp);
        a.ImagePath.Should().Be("C:\\logos\\seal.png");
        a.Text.Should().Be("APPROVED");
        a.Bounds.Should().Be(new AnnotationRect(50, 50, 150, 40));
    }

    [Fact]
    public void Import_AcrobatStyleStamp_ParsesContents()
    {
        const string xfdf = """
            <xfdf xmlns="http://ns.adobe.com/xfdf/"><annots>
              <stamp page="0" color="#FF0000" rect="50,50,250,110"><contents>DRAFT</contents></stamp>
            </annots></xfdf>
            """;

        var a = _sut.Import(xfdf).Single();

        a.Kind.Should().Be(AnnotationKind.Stamp);
        a.Text.Should().Be("DRAFT");
    }

    [Fact]
    public void Import_StampWithoutContents_IsSkipped()
    {
        // Stamp factory throws on blank label — importer must skip rather than crash.
        const string xfdf = """
            <xfdf xmlns="http://ns.adobe.com/xfdf/"><annots>
              <stamp page="0" color="#FF0000" rect="0,0,100,30" />
            </annots></xfdf>
            """;

        _sut.Import(xfdf).Should().BeEmpty();
    }
}
