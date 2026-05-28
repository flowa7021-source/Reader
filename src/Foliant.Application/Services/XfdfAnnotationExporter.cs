using System.Globalization;
using System.Xml.Linq;
using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Экспорт аннотаций в XFDF (Adobe XML Forms Data Format) — стандартный формат обмена
/// аннотациями PDF для совместного рецензирования (Q-F17). Highlight → <c>&lt;highlight&gt;</c>
/// (rect + quadpoints), StickyNote → <c>&lt;text&gt;</c> + <c>&lt;contents&gt;</c>,
/// Freehand → <c>&lt;ink&gt;</c>. Координаты — PDF user space (origin внизу-слева, Y вверх),
/// как и в <see cref="Annotation"/>. Stateless, без I/O.
/// </summary>
public sealed class XfdfAnnotationExporter : IAnnotationExporter
{
    private static readonly XNamespace Ns = "http://ns.adobe.com/xfdf/";

    public string FormatName => "XFDF";

    public string FileExtension => "xfdf";

    public string Export(IReadOnlyList<Annotation> annotations)
    {
        ArgumentNullException.ThrowIfNull(annotations);

        var annots = new XElement(Ns + "annots");
        foreach (var a in annotations)
        {
            if (ToElement(a) is { } element)
            {
                annots.Add(element);
            }
        }

        var root = new XElement(
            Ns + "xfdf",
            new XAttribute(XNamespace.Xml + "space", "preserve"),
            annots);

        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Environment.NewLine + root;
    }

    private static XElement? ToElement(Annotation a) => a.Kind switch
    {
        AnnotationKind.Highlight when a.Bounds is { } b => Highlight(a, b),
        AnnotationKind.StickyNote when a.Bounds is { } b => StickyNote(a, b),
        AnnotationKind.Freehand when a.InkPoints is { Count: > 0 } points => Ink(a, points),
        _ => null,
    };

    private static XElement Highlight(Annotation a, AnnotationRect b)
    {
        // QuadPoints: top-left, top-right, bottom-left, bottom-right (PDF order).
        string coords = Join(AnnotationGeometry.QuadPoints(b));
        return new XElement(
            Ns + "highlight",
            CommonAttributes(a),
            new XAttribute("rect", Rect(b)),
            new XAttribute("coords", coords));
    }

    private static XElement StickyNote(Annotation a, AnnotationRect b)
    {
        var element = new XElement(Ns + "text", CommonAttributes(a), new XAttribute("rect", Rect(b)));
        if (!string.IsNullOrEmpty(a.Text))
        {
            element.Add(new XElement(Ns + "contents", a.Text));
        }

        return element;
    }

    private static XElement Ink(Annotation a, IReadOnlyList<AnnotationPoint> points)
    {
        string gesture = string.Join(';', points.Select(p => F(p.X) + "," + F(p.Y)));
        return new XElement(
            Ns + "ink",
            CommonAttributes(a),
            new XElement(Ns + "inklist", new XElement(Ns + "gesture", gesture)));
    }

    private static object[] CommonAttributes(Annotation a)
    {
        // page/color/creationdate — обязательные; name/subject/date — опциональные (Acrobat
        // пишет их тоже опционально). Все три карты на /T /Subj /M в PDF аннотации.
        var attrs = new List<object>
        {
            new XAttribute("page", a.PageIndex.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("color", a.ColorHex),
            new XAttribute("creationdate", XfdfDate(a.CreatedAt)),
        };

        if (a.ModifiedAt is { } modified)
        {
            attrs.Add(new XAttribute("date", XfdfDate(modified)));
        }

        if (!string.IsNullOrEmpty(a.Author))
        {
            attrs.Add(new XAttribute("name", a.Author));
        }

        if (!string.IsNullOrEmpty(a.Subject))
        {
            attrs.Add(new XAttribute("subject", a.Subject));
        }

        return [.. attrs];
    }

    private static string XfdfDate(DateTimeOffset when) =>
        "D:" + when.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "Z";

    private static string Rect(AnnotationRect b)
    {
        var (xll, yll, xur, yur) = AnnotationGeometry.RectCorners(b);
        return Join(xll, yll, xur, yur);
    }

    private static string Join(params double[] values) => string.Join(',', values.Select(F));

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
