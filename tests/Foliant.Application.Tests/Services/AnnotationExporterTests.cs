using System.Text.Json;
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
