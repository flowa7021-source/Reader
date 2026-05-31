using System.Globalization;
using System.Xml.Linq;
using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Экспорт аннотаций в XFDF (Adobe XML Forms Data Format) — стандартный формат обмена
/// аннотациями PDF для совместного рецензирования (Q-F17). Покрытые типы:
/// <list type="bullet">
/// <item>Highlight / Underline / Strikethrough → <c>&lt;highlight&gt;</c> / <c>&lt;underline&gt;</c> /
///       <c>&lt;strikeout&gt;</c> (rect + quadpoints — общий text-markup путь).</item>
/// <item>StickyNote → <c>&lt;text&gt;</c> + <c>&lt;contents&gt;</c>.</item>
/// <item>Freehand → <c>&lt;ink&gt;</c> + <c>&lt;inklist&gt;/&lt;gesture&gt;</c>.</item>
/// <item>Rectangle / Ellipse → <c>&lt;square&gt;</c> / <c>&lt;circle&gt;</c> с rect.</item>
/// <item>Line → <c>&lt;line&gt;</c> с <c>start</c>/<c>end</c>.</item>
/// <item>Arrow → <c>&lt;line&gt;</c> + line-ending атрибуты <c>head="None" tail="OpenArrow"</c>.</item>
/// <item>Polygon → <c>&lt;polygon&gt;</c> + <c>&lt;vertices&gt;</c>.</item>
/// <item>Stamp → <c>&lt;stamp&gt;</c> + <c>&lt;contents&gt;</c> (label).</item>
/// </list>
/// Координаты — PDF user space (origin внизу-слева, Y вверх), как и в <see cref="Annotation"/>.
/// Stateless, без I/O.
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
        AnnotationKind.Highlight when a.Bounds is { } b => TextMarkup("highlight", a, b),
        AnnotationKind.Underline when a.Bounds is { } b => TextMarkup("underline", a, b),
        AnnotationKind.Strikethrough when a.Bounds is { } b => TextMarkup("strikeout", a, b),
        AnnotationKind.StickyNote when a.Bounds is { } b => StickyNote(a, b),
        AnnotationKind.Freehand when a.InkPoints is { Count: > 0 } points => Ink(a, points),
        AnnotationKind.Rectangle when a.Bounds is { } b => RectShape("square", a, b),
        AnnotationKind.Ellipse when a.Bounds is { } b => RectShape("circle", a, b),
        AnnotationKind.Line when a.InkPoints is { Count: 2 } pts => LineShape(a, pts, isArrow: false),
        AnnotationKind.Arrow when a.InkPoints is { Count: 2 } pts => LineShape(a, pts, isArrow: true),
        AnnotationKind.Polygon when a.InkPoints is { Count: >= 3 } pts => PolygonShape(a, pts),
        AnnotationKind.Stamp when a.Bounds is { } b => StampElement(a, b),
        _ => null,
    };

    private static XElement TextMarkup(string localName, Annotation a, AnnotationRect b)
    {
        // QuadPoints: top-left, top-right, bottom-left, bottom-right (PDF order).
        // Highlight / Underline / Strikeout — общий shape, отличается только локальным именем.
        string coords = Join(AnnotationGeometry.QuadPoints(b));
        return new XElement(
            Ns + localName,
            CommonAttributes(a),
            new XAttribute("rect", Rect(b)),
            new XAttribute("coords", coords));
    }

    private static XElement RectShape(string localName, Annotation a, AnnotationRect b) =>
        new(Ns + localName,
            CommonAttributes(a),
            new XAttribute("rect", Rect(b)));

    private static XElement LineShape(Annotation a, IReadOnlyList<AnnotationPoint> pts, bool isArrow)
    {
        // PDF /LE: arrow рисуется как линия с открытой стрелкой на хвосте; head="None" — без
        // ending в начале. XFDF атрибуты head/tail отражают PDF /LE [<head> <tail>].
        var element = new XElement(
            Ns + "line",
            CommonAttributes(a),
            new XAttribute("start", $"{F(pts[0].X)},{F(pts[0].Y)}"),
            new XAttribute("end", $"{F(pts[1].X)},{F(pts[1].Y)}"));
        if (isArrow)
        {
            element.Add(new XAttribute("head", "None"));
            element.Add(new XAttribute("tail", "OpenArrow"));
        }
        return element;
    }

    private static XElement PolygonShape(Annotation a, IReadOnlyList<AnnotationPoint> pts)
    {
        // Vertices пишем как "x1,y1;x2,y2;...". Acrobat принимает и space-separated, но
        // semicolon-разделение — наиболее совместимое для гетерогенных читалок.
        string vertices = string.Join(';', pts.Select(p => F(p.X) + "," + F(p.Y)));
        return new XElement(
            Ns + "polygon",
            CommonAttributes(a),
            new XElement(Ns + "vertices", vertices));
    }

    private static XElement StampElement(Annotation a, AnnotationRect b)
    {
        // PDF /Subtype /Stamp с label-текстом в /Contents. Иконка (/Name /Draft etc.) —
        // не выставляется: наши штампы — custom-label, Acrobat показывает rect-border + текст.
        var element = new XElement(Ns + "stamp", CommonAttributes(a), new XAttribute("rect", Rect(b)));
        if (!string.IsNullOrEmpty(a.Text))
        {
            element.Add(new XElement(Ns + "contents", a.Text));
        }
        return element;
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
