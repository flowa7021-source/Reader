using FluentAssertions;
using Foliant.Application.Services;
using Xunit;

namespace Foliant.Application.Tests.Services;

public sealed class FormDataFormatCatalogTests
{
    private static FormDataFormatCatalog BuildRealCatalog() => new(
        [new JsonFormDataExporter(), new FdfFormDataExporter(), new XfdfFormDataExporter()],
        [new JsonFormDataImporter(), new FdfFormDataImporter(), new XfdfFormDataImporter()]);

    [Fact]
    public void Exposes_AllRegisteredFormats_InOrder()
    {
        var catalog = BuildRealCatalog();

        catalog.Exporters.Select(e => e.FormatName).Should().Equal("JSON", "FDF", "XFDF");
        catalog.Importers.Select(i => i.FormatName).Should().Equal("JSON", "FDF", "XFDF");
    }

    [Theory]
    [InlineData("json", "JSON")]
    [InlineData("fdf", "FDF")]
    [InlineData("xfdf", "XFDF")]
    public void ResolveExporter_ByBareExtension(string ext, string expected)
    {
        BuildRealCatalog().ResolveExporter(ext)!.FormatName.Should().Be(expected);
    }

    [Theory]
    [InlineData(".XFDF")]
    [InlineData("data.xfdf")]
    [InlineData("/home/user/data.xfdf")]
    [InlineData(@"C:\Users\me\data.xfdf")]
    public void ResolveExporter_AcceptsDotPathAndCase(string input)
    {
        BuildRealCatalog().ResolveExporter(input)!.FormatName.Should().Be("XFDF");
    }

    [Fact]
    public void ResolveImporter_KnownExtensions_Found()
    {
        var catalog = BuildRealCatalog();

        catalog.ResolveImporter("data.json").Should().BeOfType<JsonFormDataImporter>();
        catalog.ResolveImporter("data.fdf").Should().BeOfType<FdfFormDataImporter>();
        catalog.ResolveImporter("data.xfdf").Should().BeOfType<XfdfFormDataImporter>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("data")]
    [InlineData("data.docx")]
    public void Unknown_ReturnsNull(string input)
    {
        BuildRealCatalog().ResolveExporter(input).Should().BeNull();
    }

    [Fact]
    public void EmptyCatalog_ResolvesToNull()
    {
        var catalog = new FormDataFormatCatalog([], []);

        catalog.Exporters.Should().BeEmpty();
        catalog.Importers.Should().BeEmpty();
        catalog.ResolveExporter("json").Should().BeNull();
        catalog.ResolveImporter("xfdf").Should().BeNull();
    }

    [Fact]
    public void NullArguments_Throw()
    {
        var act1 = () => new FormDataFormatCatalog(null!, []);
        var act2 = () => new FormDataFormatCatalog([], null!);

        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
    }
}
