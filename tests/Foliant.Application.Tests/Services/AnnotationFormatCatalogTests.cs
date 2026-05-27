using FluentAssertions;
using Foliant.Application.Services;
using Xunit;

namespace Foliant.Application.Tests.Services;

public sealed class AnnotationFormatCatalogTests
{
    private static AnnotationFormatCatalog BuildRealCatalog() => new(
        [
            new JsonAnnotationExporter(),
            new MarkdownAnnotationExporter(),
            new XfdfAnnotationExporter(),
            new FdfAnnotationExporter(),
        ],
        [new XfdfAnnotationImporter(), new JsonAnnotationImporter()]);

    [Fact]
    public void Exposes_AllRegisteredFormats_InOrder()
    {
        var catalog = BuildRealCatalog();

        catalog.Exporters.Select(e => e.FormatName)
            .Should().Equal("JSON", "Markdown", "XFDF", "FDF");
        catalog.Importers.Select(i => i.FormatName)
            .Should().Equal("XFDF", "JSON");
    }

    [Theory]
    [InlineData("json", "JSON")]
    [InlineData("md", "Markdown")]
    [InlineData("xfdf", "XFDF")]
    [InlineData("fdf", "FDF")]
    public void ResolveExporter_ByBareExtension_FindsImplementation(string ext, string expectedFormat)
    {
        BuildRealCatalog().ResolveExporter(ext)!.FormatName.Should().Be(expectedFormat);
    }

    [Theory]
    [InlineData(".XFDF")]
    [InlineData("notes.xfdf")]
    [InlineData("/home/user/notes.xfdf")]
    [InlineData(@"C:\Users\me\notes.xfdf")]
    public void ResolveExporter_AcceptsDotPathAndCase(string input)
    {
        BuildRealCatalog().ResolveExporter(input)!.FormatName.Should().Be("XFDF");
    }

    [Fact]
    public void ResolveImporter_KnownExtension_FindsImporter_UnknownReturnsNull()
    {
        var catalog = BuildRealCatalog();

        catalog.ResolveImporter("notes.xfdf").Should().BeOfType<XfdfAnnotationImporter>();
        catalog.ResolveImporter("notes.json").Should().BeOfType<JsonAnnotationImporter>();
        // FDF/Markdown have no importer registered → null, not a throw.
        catalog.ResolveImporter("notes.fdf").Should().BeNull();
        catalog.ResolveImporter("notes.md").Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("notes")]
    [InlineData("notes.")]
    [InlineData("notes.docx")]
    public void ResolveExporter_UnknownOrEmpty_ReturnsNull(string input)
    {
        BuildRealCatalog().ResolveExporter(input).Should().BeNull();
    }

    [Fact]
    public void EmptyCatalog_ResolvesToNull_AndExposesEmptyLists()
    {
        var catalog = new AnnotationFormatCatalog([], []);

        catalog.Exporters.Should().BeEmpty();
        catalog.Importers.Should().BeEmpty();
        catalog.ResolveExporter("json").Should().BeNull();
        catalog.ResolveImporter("xfdf").Should().BeNull();
    }

    [Fact]
    public void NullArguments_Throw()
    {
        var act1 = () => new AnnotationFormatCatalog(null!, []);
        var act2 = () => new AnnotationFormatCatalog([], null!);

        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
    }
}
