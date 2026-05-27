using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Xunit;

namespace Foliant.Application.Tests.Services;

public sealed class JsonAnnotationExporterTests
{
    private readonly JsonAnnotationExporter _sut = new();

    [Fact]
    public void Export_EmptyList_ReturnsValidJsonArray()
    {
        var json = _sut.Export([]);

        json.Should().Contain("[]");
        var parsed = JsonDocument.Parse(json);
        parsed.RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void Export_HighlightAndNote_SerialisesAllFields()
    {
        var hl = Annotation.Highlight(0, new AnnotationRect(1, 2, 3, 4), "#FF0", DateTimeOffset.UtcNow);
        var note = Annotation.StickyNote(2, new AnnotationRect(0, 0, 16, 16), "TODO — Привет!", "#FFCC00", DateTimeOffset.UtcNow);

        var json = _sut.Export([hl, note]);

        var parsed = JsonDocument.Parse(json);
        parsed.RootElement.GetArrayLength().Should().Be(2);
        // The serialized form must contain user data (cyrillic) so import can roundtrip later.
        json.Should().Contain("Highlight");
        json.Should().Contain("StickyNote");
        json.Should().Contain("\\u041F");   // "П" Unicode-escape (System.Text.Json default)
    }

    [Fact]
    public void FormatNameAndExtension_AreReasonable()
    {
        _sut.FormatName.Should().Be("JSON");
        _sut.FileExtension.Should().Be("json");
    }
}

public sealed class MarkdownAnnotationExporterTests
{
    private readonly MarkdownAnnotationExporter _sut = new();

    [Fact]
    public void Export_Empty_ReturnsHeaderOnly()
    {
        var md = _sut.Export([]);

        md.Should().Contain("# Annotations");
        md.Should().Contain("_No annotations._");
    }

    [Fact]
    public void Export_GroupsByPage_OneBased()
    {
        var page0 = Annotation.Highlight(0, new AnnotationRect(0, 0, 10, 10), "#FF0", DateTimeOffset.UtcNow);
        var page2 = Annotation.StickyNote(2, new AnnotationRect(0, 0, 16, 16), "Note here", "#FFC", DateTimeOffset.UtcNow);

        var md = _sut.Export([page0, page2]);

        md.Should().Contain("## Page 1");
        md.Should().Contain("## Page 3");
    }

    [Fact]
    public void Export_HighlightLine_ContainsColorHex()
    {
        var hl = Annotation.Highlight(0, new AnnotationRect(0, 0, 10, 10), "#FFEE00", DateTimeOffset.UtcNow);

        var md = _sut.Export([hl]);

        md.Should().Contain("**Highlight** (#FFEE00)");
    }

    [Fact]
    public void Export_StickyNoteLine_HasText_NewlinesFlattened()
    {
        var note = Annotation.StickyNote(0, new AnnotationRect(0, 0, 16, 16), "line one\nline two", "#FFC", DateTimeOffset.UtcNow);

        var md = _sut.Export([note]);

        md.Should().Contain("**Note**: line one line two");
        md.Should().NotContain("\nline two");
    }

    [Fact]
    public void Export_Freehand_ShowsPointCount()
    {
        var ink = Annotation.Freehand(0,
            [new AnnotationPoint(0, 0), new AnnotationPoint(1, 1), new AnnotationPoint(2, 2)],
            "#000",
            DateTimeOffset.UtcNow);

        var md = _sut.Export([ink]);

        md.Should().Contain("**Freehand** (3 points)");
    }

    [Fact]
    public void FormatNameAndExtension_AreReasonable()
    {
        _sut.FormatName.Should().Be("Markdown");
        _sut.FileExtension.Should().Be("md");
    }
}

public sealed class XfdfAnnotationExporterTests
{
    private static readonly XNamespace Ns = "http://ns.adobe.com/xfdf/";
    private readonly XfdfAnnotationExporter _sut = new();

    private static XElement ParseAnnots(string xfdf)
    {
        var doc = XDocument.Parse(xfdf);
        doc.Root!.Name.Should().Be(Ns + "xfdf");
        return doc.Root!.Element(Ns + "annots")!;
    }

    [Fact]
    public void Export_Empty_IsWellFormedWithEmptyAnnots()
    {
        var annots = ParseAnnots(_sut.Export([]));

        annots.Should().NotBeNull();
        annots.Elements().Should().BeEmpty();
    }

    [Fact]
    public void Export_Highlight_WritesRectAndQuadpoints()
    {
        var hl = Annotation.Highlight(0, new AnnotationRect(10, 20, 30, 40), "#FFEB3B", DateTimeOffset.UnixEpoch);

        var hlEl = ParseAnnots(_sut.Export([hl])).Element(Ns + "highlight")!;

        hlEl.Attribute("page")!.Value.Should().Be("0");
        hlEl.Attribute("color")!.Value.Should().Be("#FFEB3B");
        // rect = xLL,yLL,xUR,yUR = 10,20,40,60
        hlEl.Attribute("rect")!.Value.Should().Be("10,20,40,60");
        hlEl.Attribute("coords")!.Value.Should().Be("10,60,40,60,10,20,40,20");
    }

    [Fact]
    public void Export_StickyNote_WritesContentsWithUserText()
    {
        var note = Annotation.StickyNote(2, new AnnotationRect(0, 0, 16, 16), "TODO — Привет!", "#FFCC00", DateTimeOffset.UtcNow);

        var textEl = ParseAnnots(_sut.Export([note])).Element(Ns + "text")!;

        textEl.Attribute("page")!.Value.Should().Be("2");
        textEl.Element(Ns + "contents")!.Value.Should().Be("TODO — Привет!");
    }

    [Fact]
    public void Export_Freehand_WritesInklistGesture()
    {
        var ink = Annotation.Freehand(
            1,
            [new AnnotationPoint(1, 2), new AnnotationPoint(3, 4)],
            "#000000",
            DateTimeOffset.UtcNow);

        var gesture = ParseAnnots(_sut.Export([ink]))
            .Element(Ns + "ink")!.Element(Ns + "inklist")!.Element(Ns + "gesture")!;

        gesture.Value.Should().Be("1,2;3,4");
    }

    [Fact]
    public void Export_SkipsMalformedAnnotation()
    {
        // Highlight without bounds is not a valid XFDF highlight → skipped, not crashed.
        var broken = new Annotation(Guid.NewGuid(), 0, AnnotationKind.Highlight, "#FFF", null, null, null, DateTimeOffset.UtcNow);

        ParseAnnots(_sut.Export([broken])).Elements().Should().BeEmpty();
    }

    [Fact]
    public void FormatNameAndExtension_AreReasonable()
    {
        _sut.FormatName.Should().Be("XFDF");
        _sut.FileExtension.Should().Be("xfdf");
    }
}
