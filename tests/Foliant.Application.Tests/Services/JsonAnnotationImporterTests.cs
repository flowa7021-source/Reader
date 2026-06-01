using System.Text.Json;
using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Xunit;

namespace Foliant.Application.Tests.Services;

public sealed class JsonAnnotationImporterTests
{
    private readonly JsonAnnotationImporter _sut = new();

    [Fact]
    public void Roundtrip_ExportThenImport_PreservesAllFields()
    {
        var when = new DateTimeOffset(2026, 5, 27, 9, 30, 15, TimeSpan.Zero);
        var source = new List<Annotation>
        {
            Annotation.Highlight(0, new AnnotationRect(10, 20, 30, 40), "#FFEB3B", when),
            Annotation.StickyNote(2, new AnnotationRect(5, 5, 16, 16), "TODO — Привет!", "#FFCC00", when),
            Annotation.Freehand(1, [new AnnotationPoint(1, 2), new AnnotationPoint(3, 4)], "#000000", when),
        };

        var json = new JsonAnnotationExporter().Export(source);
        var imported = _sut.Import(json);

        imported.Should().HaveCount(3);
        // Unlike XFDF, JSON carries CreatedAt — only Id differs (regenerated on import).
        imported.Should().BeEquivalentTo(source, o => o.Excluding(a => a.Id));
        imported.Select(a => a.Id).Should().OnlyHaveUniqueItems()
            .And.NotContain(source.Select(a => a.Id));
    }

    [Fact]
    public void Import_RegeneratesId_EvenWhenJsonCarriesOne()
    {
        var original = Annotation.Highlight(0, new AnnotationRect(1, 2, 3, 4), "#FF0000", DateTimeOffset.UnixEpoch);
        var json = new JsonAnnotationExporter().Export([original]);

        var imported = _sut.Import(json).Single();

        imported.Id.Should().NotBe(original.Id);
    }

    [Fact]
    public void Import_SkipsMalformedElements_KeepsValidOnes()
    {
        const string json = """
            [
              { "Kind": "Highlight", "ColorHex": "#FFF", "PageIndex": 0 },
              { "Kind": "Bogus", "ColorHex": "#FFF", "PageIndex": 0, "Bounds": { "X": 0, "Y": 0, "Width": 1, "Height": 1 } },
              { "Kind": "Freehand", "ColorHex": "#000", "PageIndex": 1, "InkPoints": [ { "X": 0, "Y": 0 } ] },
              { "Kind": "Highlight", "ColorHex": "#0F0", "PageIndex": 3, "Bounds": { "X": 0, "Y": 0, "Width": 10, "Height": 10 } }
            ]
            """;

        var imported = _sut.Import(json);

        // Only the last element is valid: #1 highlight lacks Bounds, #2 unknown kind, #3 freehand <2 points.
        imported.Should().ContainSingle();
        imported[0].PageIndex.Should().Be(3);
        imported[0].ColorHex.Should().Be("#0F0");
    }

    [Fact]
    public void Import_NonArrayRoot_ReturnsEmpty()
    {
        _sut.Import("{ \"Kind\": \"Highlight\" }").Should().BeEmpty();
    }

    [Fact]
    public void Import_EmptyArray_ReturnsEmpty()
    {
        _sut.Import("[]").Should().BeEmpty();
    }

    [Fact]
    public void Import_MalformedJson_Throws()
    {
        var act = () => _sut.Import("[ { \"Kind\":");

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void FormatNameAndExtension_AreReasonable()
    {
        _sut.FormatName.Should().Be("JSON");
        _sut.FileExtension.Should().Be("json");
    }

    [Fact]
    public void Roundtrip_WithMetadata_PreservesAuthorSubjectModifiedAt()
    {
        var created = new DateTimeOffset(2024, 1, 15, 10, 30, 45, TimeSpan.Zero);
        var modified = new DateTimeOffset(2024, 6, 20, 16, 0, 0, TimeSpan.Zero);
        var source = new[]
        {
            Annotation.StickyNote(0, new AnnotationRect(1, 2, 3, 4), "note", "#FF0000", created) with
            {
                ModifiedAt = modified,
                Author = "Reviewer",
                Subject = "Question",
            },
        };

        var json = new JsonAnnotationExporter().Export(source);
        var imported = _sut.Import(json);

        var a = imported.Should().ContainSingle().Subject;
        a.Author.Should().Be("Reviewer");
        a.Subject.Should().Be("Question");
        a.ModifiedAt.Should().Be(modified);
    }

    // ───── Q-F18 A5: image-stamp ImagePath round-trip ─────

    [Fact]
    public void Roundtrip_ImageStamp_PreservesImagePathAndLabel()
    {
        var source = new[]
        {
            Annotation.ImageStamp(
                pageIndex: 2,
                bounds: new AnnotationRect(100, 100, 200, 60),
                imagePath: "C:\\logos\\company.png",
                label: "APPROVED",
                colorHex: "#00AA00",
                createdAt: DateTimeOffset.UnixEpoch),
        };

        var imported = _sut.Import(new JsonAnnotationExporter().Export(source));

        var a = imported.Should().ContainSingle().Subject;
        a.Kind.Should().Be(AnnotationKind.Stamp);
        a.ImagePath.Should().Be("C:\\logos\\company.png");
        a.Text.Should().Be("APPROVED");
        a.ColorHex.Should().Be("#00AA00");
    }

    [Fact]
    public void Roundtrip_TextStamp_ImagePathIsNull()
    {
        var source = new[]
        {
            Annotation.Stamp(0, new AnnotationRect(0, 0, 100, 30), "DRAFT", "#FF0000", DateTimeOffset.UnixEpoch),
        };

        var imported = _sut.Import(new JsonAnnotationExporter().Export(source));

        imported.Should().ContainSingle().Subject.ImagePath.Should().BeNull();
    }
}
