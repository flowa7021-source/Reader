using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Xunit;

namespace Foliant.Application.Tests.Services;

/// <summary>
/// Track A5c: `Annotation.ImagePath` round-trip через FDF custom-ключ
/// <c>/FoliantImagePath</c>. Базовые (a/b) уже покрыты в <see cref="FdfAnnotationImporterTests"/>;
/// здесь — углы (c) ImagePath применим только к Stamp; (d) Unicode и PDF-чувствительные
/// символы в пути (FDF text-string эмитится как UTF-16BE hex, что нейтрализует и
/// `(`/`)`/`\` PDF-литералы, и любые XML-метасимволы).
/// </summary>
public sealed class FdfImagePathRoundTripTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly FdfAnnotationExporter _exporter = new();
    private readonly FdfAnnotationImporter _importer = new();

    [Fact]
    public void RoundTrip_NonStampAnnotations_NeverCarryImagePath()
    {
        AnnotationPoint[] linePts = [new AnnotationPoint(0, 0), new AnnotationPoint(10, 10)];
        AnnotationPoint[] polyPts =
        [
            new AnnotationPoint(0, 0), new AnnotationPoint(10, 0),
            new AnnotationPoint(10, 10),
        ];

        var source = new Annotation[]
        {
            Annotation.Highlight(0, new AnnotationRect(0, 0, 10, 10), "#FF0000", T0),
            Annotation.Underline(0, new AnnotationRect(0, 0, 10, 10), "#FF0000", T0),
            Annotation.Strikethrough(0, new AnnotationRect(0, 0, 10, 10), "#FF0000", T0),
            Annotation.StickyNote(0, new AnnotationRect(0, 0, 16, 16), "note", "#FFCC00", T0),
            Annotation.Freehand(0, [new AnnotationPoint(1, 1), new AnnotationPoint(2, 2)], "#000", T0),
            Annotation.Rectangle(0, new AnnotationRect(0, 0, 10, 10), "#000", T0),
            Annotation.Ellipse(0, new AnnotationRect(0, 0, 10, 10), "#000", T0),
            Annotation.Line(0, linePts, "#000", T0),
            Annotation.Arrow(0, linePts, "#000", T0),
            Annotation.Polygon(0, polyPts, "#000", T0),
        };

        var imported = _importer.Import(_exporter.Export(source));

        imported.Should().HaveCount(source.Length);
        imported.Should().OnlyContain(a => a.ImagePath == null);
    }

    [Fact]
    public void RoundTrip_ImagePathWithUnicode_PreservesExactly()
    {
        // Кириллица + non-BMP code point (U+1F4C4) — UTF-16BE hex-стринги гарантируют
        // bit-exact round-trip независимо от платформы.
        const string path = "/Users/Иван/Документы/\U0001F4C4/печать.png";
        var src = new[]
        {
            Annotation.ImageStamp(0, new AnnotationRect(0, 0, 100, 50), path, "stamp", "#000", T0),
        };

        var a = _importer.Import(_exporter.Export(src)).Should().ContainSingle().Subject;

        a.ImagePath.Should().Be(path);
    }

    [Fact]
    public void RoundTrip_ImagePathWithPdfAndXmlSpecialChars_PreservesExactly()
    {
        // `(`/`)`/`\` — PDF literal-string delimiters; `&`/`<`/`>` — XML metachars; пробелы —
        // path-frequent. Все они должны выйти через UTF-16BE hex без потерь.
        const string path = """C:\path (with) spaces\file&name<x>"y"'z'.png""";
        var src = new[]
        {
            Annotation.ImageStamp(0, new AnnotationRect(0, 0, 100, 50), path, "stamp", "#000", T0),
        };

        string fdf = _exporter.Export(src);
        var a = _importer.Import(fdf).Should().ContainSingle().Subject;

        a.ImagePath.Should().Be(path);

        // Sanity: сырых литералов "(" / ")" в hex-стринге не появляется — все text-string'и
        // экспортёр пишет в hex-форме `<FEFF...>`. Проверяем, что путь не вылез как literal-(...).
        fdf.Should().NotContain("(file&name");
    }

    [Fact]
    public void Export_StampWithoutImagePath_OmitsKey()
    {
        // Текстовый stamp не должен загромождать FDF /FoliantImagePath — ключ просто отсутствует.
        var src = new[]
        {
            Annotation.Stamp(0, new AnnotationRect(0, 0, 100, 50), "DRAFT", "#000", T0),
        };

        string fdf = _exporter.Export(src);

        fdf.Should().NotContain("FoliantImagePath");
    }
}
