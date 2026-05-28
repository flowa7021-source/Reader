using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Xunit;

namespace Foliant.Application.Tests.Services;

public sealed class XfdfBookmarkRoundTripTests
{
    private readonly XfdfBookmarkExporter _exporter = new();
    private readonly XfdfBookmarkImporter _importer = new();

    [Fact]
    public void Roundtrip_FlatBookmarks_PreservesPagesAndLabelsAndDepthZero()
    {
        var when = new DateTimeOffset(2025, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var source = new List<Bookmark>
        {
            new(Guid.NewGuid(), 0, "Cover", when),
            new(Guid.NewGuid(), 4, "Chapter 1", when),
            new(Guid.NewGuid(), 9, "Chapter 2", when),
        };

        var imported = _importer.Import(_exporter.Export(source));

        imported.Should().HaveCount(3);
        imported.Select(b => (b.PageIndex, b.Label, b.Depth))
            .Should().Equal(
                (0, "Cover", 0),
                (4, "Chapter 1", 0),
                (9, "Chapter 2", 0));
    }

    [Fact]
    public void Roundtrip_NestedBookmarks_PreservesDepthAndPreOrder()
    {
        var when = DateTimeOffset.UnixEpoch;
        var source = new List<Bookmark>
        {
            new(Guid.NewGuid(), 1, "Chapter 1", when, Depth: 0),
            new(Guid.NewGuid(), 1, "Section 1.1", when, Depth: 1),
            new(Guid.NewGuid(), 2, "Sub 1.1.1", when, Depth: 2),
            new(Guid.NewGuid(), 5, "Chapter 2", when, Depth: 0),
        };

        var imported = _importer.Import(_exporter.Export(source));

        imported.Select(b => (b.PageIndex, b.Label, b.Depth))
            .Should().Equal(
                (1, "Chapter 1", 0),
                (1, "Section 1.1", 1),
                (2, "Sub 1.1.1", 2),
                (5, "Chapter 2", 0));
    }

    [Fact]
    public void Export_EmitsAdobeNamespaceAndBookmarkTree()
    {
        var xml = _exporter.Export([new Bookmark(Guid.NewGuid(), 0, "x", DateTimeOffset.UnixEpoch)]);

        xml.Should().Contain("xmlns=\"http://ns.adobe.com/xfdf/\"");
        xml.Should().Contain("<bookmark-tree");
        xml.Should().Contain("Title=\"x\"");
    }

    [Fact]
    public void Roundtrip_PreservesCyrillicLabels()
    {
        var source = new List<Bookmark>
        {
            new(Guid.NewGuid(), 0, "Глава — Введение", DateTimeOffset.UnixEpoch),
        };

        var roundtripped = _importer.Import(_exporter.Export(source)).Should().ContainSingle().Subject;

        roundtripped.Label.Should().Be("Глава — Введение");
    }

    [Fact]
    public void Import_AcceptsLowercaseTitleAttribute_AndAdobeStyleDestElement()
    {
        // Рукотворный XFDF: lowercase "title" + <Dest>-stiль страница 1-based.
        // Должен read'иться как закладка на page=2 (1-based "3" → 0-based 2).
        string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <xfdf xmlns="http://ns.adobe.com/xfdf/" xml:space="preserve">
              <bookmark-tree>
                <bookmark title="Intro">
                  <Dest>[ 3 /Fit ]</Dest>
                </bookmark>
              </bookmark-tree>
            </xfdf>
            """;

        var imported = _importer.Import(xml).Should().ContainSingle().Subject;

        imported.Label.Should().Be("Intro");
        imported.PageIndex.Should().Be(2);
    }

    [Fact]
    public void Import_SkipsNodesMissingTitleOrPage()
    {
        string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <xfdf xmlns="http://ns.adobe.com/xfdf/">
              <bookmark-tree>
                <bookmark Title="No page" />
                <bookmark page="2" />
                <bookmark Title="Valid" page="4" />
              </bookmark-tree>
            </xfdf>
            """;

        var imported = _importer.Import(xml);

        imported.Should().ContainSingle()
            .Which.Label.Should().Be("Valid");
    }

    [Fact]
    public void Import_MalformedXml_Throws()
    {
        var act = () => _importer.Import("not-xml");

        act.Should().Throw<System.Xml.XmlException>();
    }

    [Fact]
    public void FormatAndExtension_AreReasonable()
    {
        _exporter.FormatName.Should().Be("XFDF");
        _exporter.FileExtension.Should().Be("xfdf");
        _importer.FormatName.Should().Be("XFDF");
        _importer.FileExtension.Should().Be("xfdf");
    }
}
