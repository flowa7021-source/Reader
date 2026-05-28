using FluentAssertions;
using Foliant.Application.Services;
using Xunit;

namespace Foliant.Application.Tests.Services;

public sealed class BookmarkFormatCatalogTests
{
    private static BookmarkFormatCatalog BuildRealCatalog() => new(
        [new JsonBookmarkExporter(), new MarkdownBookmarkExporter(), new XfdfBookmarkExporter()],
        [new JsonBookmarkImporter(), new XfdfBookmarkImporter()]);

    [Fact]
    public void Exposes_AllRegisteredFormats_InOrder()
    {
        var catalog = BuildRealCatalog();

        catalog.Exporters.Select(e => e.FormatName).Should().Equal("JSON", "Markdown", "XFDF");
        catalog.Importers.Select(i => i.FormatName).Should().Equal("JSON", "XFDF");
    }

    [Theory]
    [InlineData("json", "JSON")]
    [InlineData("md", "Markdown")]
    [InlineData("xfdf", "XFDF")]
    public void ResolveExporter_ByBareExtension_FindsImplementation(string ext, string expectedFormat)
    {
        BuildRealCatalog().ResolveExporter(ext)!.FormatName.Should().Be(expectedFormat);
    }

    [Theory]
    [InlineData(".JSON")]
    [InlineData("bookmarks.json")]
    [InlineData("/home/user/bookmarks.json")]
    [InlineData(@"C:\Users\me\bookmarks.json")]
    public void ResolveExporter_AcceptsDotPathAndCase(string input)
    {
        BuildRealCatalog().ResolveExporter(input)!.FormatName.Should().Be("JSON");
    }

    [Fact]
    public void ResolveImporter_KnownExtension_FindsImporter_UnknownReturnsNull()
    {
        var catalog = BuildRealCatalog();

        catalog.ResolveImporter("bookmarks.json").Should().BeOfType<JsonBookmarkImporter>();
        catalog.ResolveImporter("bookmarks.xfdf").Should().BeOfType<XfdfBookmarkImporter>();
        // Markdown — экспорт-only по дизайну (lossy для импорта); ожидаемо null.
        catalog.ResolveImporter("bookmarks.md").Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bookmarks")]
    [InlineData("bookmarks.")]
    [InlineData("bookmarks.docx")]
    public void ResolveExporter_UnknownOrEmpty_ReturnsNull(string input)
    {
        BuildRealCatalog().ResolveExporter(input).Should().BeNull();
    }

    [Fact]
    public void EmptyCatalog_ResolvesToNull_AndExposesEmptyLists()
    {
        var catalog = new BookmarkFormatCatalog([], []);

        catalog.Exporters.Should().BeEmpty();
        catalog.Importers.Should().BeEmpty();
        catalog.ResolveExporter("json").Should().BeNull();
        catalog.ResolveImporter("json").Should().BeNull();
    }

    [Fact]
    public void NullArguments_Throw()
    {
        var act1 = () => new BookmarkFormatCatalog(null!, []);
        var act2 = () => new BookmarkFormatCatalog([], null!);

        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
    }
}
