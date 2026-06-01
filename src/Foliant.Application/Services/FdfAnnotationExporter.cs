using System.Globalization;
using System.Text;
using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Экспорт аннотаций в FDF (Adobe Forms Data Format) — легаси-формат обмена на PDF-синтаксисе
/// (Q-F17), читается Acrobat. Покрытые типы:
/// <list type="bullet">
/// <item>Highlight / Underline / Strikethrough → <c>/Subtype /Highlight | /Underline | /StrikeOut</c>
///       (общий text-markup путь с <c>/QuadPoints</c>).</item>
/// <item>StickyNote → <c>/Subtype /Text</c> + <c>/Contents</c>.</item>
/// <item>Freehand → <c>/Subtype /Ink</c> + <c>/InkList</c>.</item>
/// <item>Rectangle / Ellipse → <c>/Subtype /Square | /Circle</c> + <c>/Rect</c>.</item>
/// <item>Line → <c>/Subtype /Line</c> + <c>/L [x1 y1 x2 y2]</c>.</item>
/// <item>Arrow → <c>/Subtype /Line</c> + <c>/LE [/None /OpenArrow]</c>.</item>
/// <item>Polygon → <c>/Subtype /Polygon</c> + <c>/Vertices [x1 y1 x2 y2 …]</c>.</item>
/// <item>Stamp → <c>/Subtype /Stamp</c> + <c>/Rect</c> + <c>/Contents</c> (label-текст);
///       image-stamps дополнительно пишут custom <c>/FoliantImagePath</c> (A5c).</item>
/// </list>
/// Координаты — PDF user space; цвет — массив <c>[r g b]</c> 0..1; текст —
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
        AnnotationKind.Highlight when a.Bounds is { } b => TextMarkupDict("Highlight", a, b),
        AnnotationKind.Underline when a.Bounds is { } b => TextMarkupDict("Underline", a, b),
        AnnotationKind.Strikethrough when a.Bounds is { } b => TextMarkupDict("StrikeOut", a, b),
        AnnotationKind.StickyNote when a.Bounds is { } b =>
            $"<< /Type /Annot /Subtype /Text {PageRectColor(a, b)} /Contents {PdfText(a.Text)}{Metadata(a)} >>",
        AnnotationKind.Freehand when a.InkPoints is { Count: > 0 } points =>
            $"<< /Type /Annot /Subtype /Ink /Page {Page(a)} {ColorEntry(a.ColorHex)} /InkList [[{InkList(points)}]]{Metadata(a)} >>",
        AnnotationKind.Rectangle when a.Bounds is { } b =>
            $"<< /Type /Annot /Subtype /Square {PageRectColor(a, b)}{Metadata(a)} >>",
        AnnotationKind.Ellipse when a.Bounds is { } b =>
            $"<< /Type /Annot /Subtype /Circle {PageRectColor(a, b)}{Metadata(a)} >>",
        AnnotationKind.Line when a.InkPoints is { Count: 2 } pts =>
            LineDict(a, pts, isArrow: false),
        AnnotationKind.Arrow when a.InkPoints is { Count: 2 } pts =>
            LineDict(a, pts, isArrow: true),
        AnnotationKind.Polygon when a.InkPoints is { Count: >= 3 } pts =>
            $"<< /Type /Annot /Subtype /Polygon /Page {Page(a)} {ColorEntry(a.ColorHex)} /Vertices [{Vertices(pts)}]{Metadata(a)} >>",
        AnnotationKind.Stamp when a.Bounds is { } b =>
            $"<< /Type /Annot /Subtype /Stamp {PageRectColor(a, b)} /Contents {PdfText(a.Text)}{ImagePathEntry(a)}{Metadata(a)} >>",
        _ => null,
    };

    // Image-stamp (A5c): путь к изображению — в custom-ключ /FoliantImagePath (PDF-словари
    // допускают произвольные ключи; Acrobat игнорирует незнакомый, Foliant round-trip'ит).
    // Пустой/null ImagePath → ключ не пишется.
    private static string ImagePathEntry(Annotation a) =>
        string.IsNullOrEmpty(a.ImagePath) ? string.Empty : $" /FoliantImagePath {PdfText(a.ImagePath)}";

    private static string TextMarkupDict(string subtype, Annotation a, AnnotationRect b) =>
        $"<< /Type /Annot /Subtype /{subtype} {PageRectColor(a, b)} /QuadPoints [{QuadPoints(b)}]{Metadata(a)} >>";

    private static string LineDict(Annotation a, IReadOnlyList<AnnotationPoint> pts, bool isArrow)
    {
        // PDF /L массив: [x1 y1 x2 y2]. /LE [head tail] управляет окончаниями.
        // Arrow: head=None, tail=OpenArrow (соответствует XfdfAnnotationExporter).
        string l = $"/L [{F(pts[0].X)} {F(pts[0].Y)} {F(pts[1].X)} {F(pts[1].Y)}]";
        string le = isArrow ? " /LE [/None /OpenArrow]" : string.Empty;
        return $"<< /Type /Annot /Subtype /Line /Page {Page(a)} {ColorEntry(a.ColorHex)} {l}{le}{Metadata(a)} >>";
    }

    private static string Vertices(IReadOnlyList<AnnotationPoint> pts) =>
        string.Join(' ', pts.Select(p => $"{F(p.X)} {F(p.Y)}"));

    // Метаданные пишем только если поля заполнены — пустые /T/Subj бесполезны, /M/CreationDate
    // имеют смысл лишь как реальные временные метки. /CreationDate всегда есть (домен требует),
    // /M пишется только при наличии ModifiedAt.
    private static string Metadata(Annotation a)
    {
        var sb = new StringBuilder();
        sb.Append(" /CreationDate ").Append(PdfDate(a.CreatedAt));
        if (a.ModifiedAt is { } m)
        {
            sb.Append(" /M ").Append(PdfDate(m));
        }

        if (!string.IsNullOrEmpty(a.Author))
        {
            sb.Append(" /T ").Append(PdfText(a.Author));
        }

        if (!string.IsNullOrEmpty(a.Subject))
        {
            sb.Append(" /Subj ").Append(PdfText(a.Subject));
        }

        return sb.ToString();
    }

    private static string PdfDate(DateTimeOffset when)
    {
        var utc = when.ToUniversalTime();
        return $"(D:{utc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}Z)";
    }

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
