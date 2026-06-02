using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Xunit;

namespace Foliant.Application.Tests.Services;

/// <summary>
/// Track A5c: `Annotation.ImagePath` round-trip через XFDF custom-атрибут
/// <c>foliant:imagepath</c>. Базовые (a/b) уже покрыты в <see cref="XfdfAnnotationImporterTests"/>;
/// здесь — углы (c) ImagePath применим только к Stamp; (d) экранирование XML-чувствительных
/// символов и Unicode в пути.
/// </summary>
public sealed class XfdfImagePathRoundTripTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly XfdfAnnotationExporter _exporter = new();
    private readonly XfdfAnnotationImporter _importer = new();

    [Fact]
    public void RoundTrip_NonStampAnnotations_NeverCarryImagePath()
    {
        // ImagePath применим только к Stamp; экспортёр не должен эмитить атрибут
        // ни для одного другого kind. Импортёр со своей стороны не вызывает ImageStamp-фабрику
        // для не-stamp элементов — даже если атрибут как-то попадёт, он не материализуется.
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
        // Кириллица + non-BMP code point (U+1F4C4) — оба должны round-trip'нуться через XML.
        const string path = "/Users/Иван/Документы/\U0001F4C4/печать.png";
        var src = new[]
        {
            Annotation.ImageStamp(0, new AnnotationRect(0, 0, 100, 50), path, "stamp", "#000", T0),
        };

        var a = _importer.Import(_exporter.Export(src)).Should().ContainSingle().Subject;

        a.ImagePath.Should().Be(path);
    }

    [Fact]
    public void RoundTrip_ImagePathWithXmlSpecialChars_PreservesExactly()
    {
        // `&`, `<`, `>`, `"`, `'` — XML-чувствительны. XAttribute должен эскейпить их в выводе
        // и парсер должен раскрывать обратно — буква-в-букву.
        const string path = """C:\path with spaces\file&name<x>"y"'z'.png""";
        var src = new[]
        {
            Annotation.ImageStamp(0, new AnnotationRect(0, 0, 100, 50), path, "stamp", "#000", T0),
        };

        string xfdf = _exporter.Export(src);
        var a = _importer.Import(xfdf).Should().ContainSingle().Subject;

        a.ImagePath.Should().Be(path);

        // Sanity: сырой `<` НЕ должен встретиться в значении атрибута (только в разметке).
        // Lower-bound — XAttribute эскейпит как минимум `<` → `&lt;` и `&` → `&amp;`.
        xfdf.Should().Contain("&lt;").And.Contain("&amp;");
    }

    [Fact]
    public void Export_StampWithoutImagePath_OmitsAttribute()
    {
        // Текстовый stamp не должен загромождать XFDF foliant:imagepath="" — атрибут просто
        // отсутствует. Это удешевляет diff/eyeballing экспортов и не путает сторонних читалок.
        var src = new[]
        {
            Annotation.Stamp(0, new AnnotationRect(0, 0, 100, 50), "DRAFT", "#000", T0),
        };

        string xfdf = _exporter.Export(src);

        xfdf.Should().NotContain("imagepath");
    }
}
