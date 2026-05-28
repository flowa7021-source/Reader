using System.Globalization;
using System.Text;
using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Экспорт аннотаций в FDF (Adobe Forms Data Format) — легаси-формат обмена на PDF-синтаксисе
/// (Q-F17), читается Acrobat. Highlight/StickyNote(/Text)/Freehand(/Ink) — PDF-словари в массиве
/// <c>/Annots</c>. Координаты — PDF user space; цвет — массив <c>[r g b]</c> 0..1; текст —
/// UTF-16BE hex-строка (корректно для кириллицы). Stateless, без I/O.
///
/// Round-trip: импорт обратно делает <see cref="FdfAnnotationImporter"/>.
/// </summary>
public sealed class FdfAnnotationExporter : IAnnotationExporter
{
    public string FormatName => "FDF";

    public string FileExtension => "fdf";

    public string Export(IReadOnlyList<Annotation> annotations)
    {
        ArgumentNullException.ThrowIfNull(annotations);

        var sb = new StringBuilder();
        sb.Append("%FDF-1.2\n1 0 obj\n<<\n/FDF\n<<\n/Annots [\n");
        foreach (var a in annotations)
        {
            if (AnnotDict(a) is { } dict)
            {
                sb.Append(dict).Append('\n');
            }
        }

        sb.Append("]\n>>\n>>\nendobj\ntrailer\n<<\n/Root 1 0 R\n>>\n%%EOF\n");
        return sb.ToString();
    }

    private static string? AnnotDict(Annotation a) => a.Kind switch
    {
        AnnotationKind.Highlight when a.Bounds is { } b =>
            $"<< /Type /Annot /Subtype /Highlight {PageRectColor(a, b)} /QuadPoints [{QuadPoints(b)}] >>",
        AnnotationKind.StickyNote when a.Bounds is { } b =>
            $"<< /Type /Annot /Subtype /Text {PageRectColor(a, b)} /Contents {PdfText(a.Text)} >>",
        AnnotationKind.Freehand when a.InkPoints is { Count: > 0 } points =>
            $"<< /Type /Annot /Subtype /Ink /Page {Page(a)} {ColorEntry(a.ColorHex)} /InkList [[{InkList(points)}]] >>",
        _ => null,
    };

    private static string PageRectColor(Annotation a, AnnotationRect b) =>
        $"/Page {Page(a)} /Rect [{Rect(b)}] {ColorEntry(a.ColorHex)}";

    private static string Page(Annotation a) => a.PageIndex.ToString(CultureInfo.InvariantCulture);

    private static string Rect(AnnotationRect b)
    {
        var (xll, yll, xur, yur) = AnnotationGeometry.RectCorners(b);
        return $"{F(xll)} {F(yll)} {F(xur)} {F(yur)}";
    }

    private static string QuadPoints(AnnotationRect b) =>
        string.Join(' ', AnnotationGeometry.QuadPoints(b).Select(F));

    private static string InkList(IReadOnlyList<AnnotationPoint> points) =>
        string.Join(' ', points.Select(p => $"{F(p.X)} {F(p.Y)}"));

    private static string ColorEntry(string colorHex)
    {
        (double r, double g, double b) = ParseColor(colorHex);
        return $"/C [{F(r)} {F(g)} {F(b)}]";
    }

    private static (double R, double G, double B) ParseColor(string hex) =>
        HexColor.TryParse(hex, out byte r, out byte g, out byte b)
            ? (r / 255.0, g / 255.0, b / 255.0)
            : (0, 0, 0);

    // PDF text string как UTF-16BE hex с BOM — единственный переносимый способ для не-ASCII.
    private static string PdfText(string? text)
    {
        byte[] bytes = Encoding.BigEndianUnicode.GetBytes(text ?? string.Empty);
        var sb = new StringBuilder("<FEFF");
        foreach (byte b in bytes)
        {
            sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }

        return sb.Append('>').ToString();
    }

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
