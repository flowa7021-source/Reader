using System.Text.Json;
using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Xunit;

namespace Foliant.Application.Tests.Services;

public sealed class JsonBookmarkImporterTests
{
    private readonly JsonBookmarkImporter _sut = new();

    [Fact]
    public void Roundtrip_ExportThenImport_PreservesFieldsExceptId()
    {
        var when = new DateTimeOffset(2026, 5, 27, 9, 30, 15, TimeSpan.Zero);
        var source = new List<Bookmark>
        {
            Bookmark.Create(0, "Title page", when),
            Bookmark.Create(3, "Глава — Введение", when),
        };

        var json = new JsonBookmarkExporter().Export(source);
        var imported = _sut.Import(json);

        imported.Should().HaveCount(2);
        imported.Should().BeEquivalentTo(source, o => o.Excluding(b => b.Id));
        imported.Select(b => b.Id).Should().OnlyHaveUniqueItems()
            .And.NotContain(source.Select(b => b.Id));
    }

    [Fact]
    public void Import_RegeneratesId_EvenWhenJsonCarriesOne()
    {
        var original = Bookmark.Create(2, "Chapter", DateTimeOffset.UnixEpoch);
        var json = new JsonBookmarkExporter().Export([original]);

        _sut.Import(json).Single().Id.Should().NotBe(original.Id);
    }

    [Fact]
    public void Import_SkipsMalformedElements_KeepsValidOnes()
    {
        const string json = """
            [
              { "PageIndex": -1, "Label": "negative page" },
              { "PageIndex": 0, "Label": "   " },
              { "PageIndex": 5, "Label": "Good one" }
            ]
            """;

        var imported = _sut.Import(json);

        imported.Should().ContainSingle();
        imported[0].PageIndex.Should().Be(5);
        imported[0].Label.Should().Be("Good one");
    }

    [Fact]
    public void Import_NonArrayRoot_ReturnsEmpty()
    {
        _sut.Import("{ \"PageIndex\": 0 }").Should().BeEmpty();
    }

    [Fact]
    public void Import_MalformedJson_Throws()
    {
        var act = () => _sut.Import("[ { \"PageIndex\":");

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void FormatNameAndExtension_AreReasonable()
    {
        _sut.FormatName.Should().Be("JSON");
        _sut.FileExtension.Should().Be("json");
    }
}
