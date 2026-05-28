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
}
