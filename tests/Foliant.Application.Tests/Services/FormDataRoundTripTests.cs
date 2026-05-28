using FluentAssertions;
using Foliant.Application.Services;
using Xunit;

namespace Foliant.Application.Tests.Services;

public sealed class FormDataRoundTripTests
{
    private static readonly Dictionary<string, string> Sample = new(StringComparer.Ordinal)
    {
        ["FullName"] = "Иван Петров",
        ["Email"] = "ivan@example.com",
        ["Age"] = "42",
        ["Notes"] = "Line one\nLine two — with dash",
    };

    [Fact]
    public void Json_Roundtrip_PreservesAllFields()
    {
        var exporter = new JsonFormDataExporter();
        var importer = new JsonFormDataImporter();

        var imported = importer.Import(exporter.Export(Sample));

        imported.Should().BeEquivalentTo(Sample);
    }

    [Fact]
    public void Fdf_Roundtrip_PreservesAllFields_IncludingCyrillicAndNewlines()
    {
        var exporter = new FdfFormDataExporter();
        var importer = new FdfFormDataImporter();

        var imported = importer.Import(exporter.Export(Sample));

        imported.Should().BeEquivalentTo(Sample);
    }

    [Fact]
    public void Xfdf_Roundtrip_PreservesAllFields()
    {
        var exporter = new XfdfFormDataExporter();
        var importer = new XfdfFormDataImporter();

        var imported = importer.Import(exporter.Export(Sample));

        imported.Should().BeEquivalentTo(Sample);
    }

    [Fact]
    public void Json_Import_NonStringValues_AreSkipped()
    {
        // {"a": "x", "b": 42, "c": true, "d": null, "e": "ok"} — оставляем только "a" и "e".
        string json = """{"a":"x","b":42,"c":true,"d":null,"e":"ok"}""";

        var imported = new JsonFormDataImporter().Import(json);

        imported.Should().BeEquivalentTo(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["a"] = "x",
            ["e"] = "ok",
        });
    }

    [Fact]
    public void Json_Import_NotAnObject_ReturnsEmpty()
    {
        new JsonFormDataImporter().Import("[]").Should().BeEmpty();
    }

    [Fact]
    public void Fdf_Import_MissingHeader_Throws()
    {
        var act = () => new FdfFormDataImporter().Import("not an fdf");

        act.Should().Throw<FormatException>().WithMessage("*%FDF-*");
    }

    [Fact]
    public void Xfdf_Import_NoFieldsElement_ReturnsEmpty()
    {
        string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <xfdf xmlns="http://ns.adobe.com/xfdf/" />
            """;

        new XfdfFormDataImporter().Import(xml).Should().BeEmpty();
    }

    [Fact]
    public void Xfdf_Import_FieldWithoutName_IsSkipped()
    {
        string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <xfdf xmlns="http://ns.adobe.com/xfdf/">
              <fields>
                <field><value>orphan</value></field>
                <field name="ok"><value>kept</value></field>
              </fields>
            </xfdf>
            """;

        var imported = new XfdfFormDataImporter().Import(xml);

        imported.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new KeyValuePair<string, string>("ok", "kept"));
    }

    [Fact]
    public void All_Exporters_HaveDistinctExtensions()
    {
        IFormDataExporter[] exporters = [new JsonFormDataExporter(), new FdfFormDataExporter(), new XfdfFormDataExporter()];

        exporters.Select(e => e.FileExtension).Distinct().Should().HaveCount(exporters.Length);
    }
}
