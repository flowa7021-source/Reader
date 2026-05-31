using System.Globalization;
using System.Xml.Linq;
using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Импорт аннотаций из XFDF (Q-F17) — обратная операция к <see cref="XfdfAnnotationExporter"/>.
/// Элементы ищутся по локальному имени (highlight / underline / strikeout / text / ink /
/// square / circle / line / polygon / stamp) независимо от namespace, чтобы принимать XFDF
/// из Acrobat и других инструментов. Arrow распознаётся как <c>&lt;line&gt;</c> с не-<c>None</c>
/// <c>tail</c>-атрибутом (например <c>tail="OpenArrow"</c>). Битые/неполные элементы
/// пропускаются; невалидный XML бросает исключение вызывающему.
/// </summary>
public sealed class XfdfAnnotationImporter : IAnnotationImporter
{
    public string FormatName => "XFDF";

    public string FileExtension => "xfdf";

    public IReadOnlyList<Annotation> Import(string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var doc = XDocument.Parse(content);
        var result = new List<Annotation>();
        foreach (var el in doc.Descendants())
        {
            Annotation? annotation = el.Name.LocalName switch
            {
                "highlight" => ParseTextMarkup(el, AnnotationKind.Highlight),
                "underline" => ParseTextMarkup(el, AnnotationKind.Underline),
                "strikeout" => ParseTextMarkup(el, AnnotationKind.Strikethrough),
                "text" => ParseText(el),
                "ink" => ParseInk(el),
                "square" => ParseRectShape(el, AnnotationKind.Rectangle),
                "circle" => ParseRectShape(el, AnnotationKind.Ellipse),
                "line" => ParseLine(el),
                "polygon" => ParsePolygon(el),
                "stamp" => ParseStamp(el),
                _ => null,
            };

            if (annotation is not null)
            {
                result.Add(annotation);
            }
        }

        return result;
    }

    private static Annotation? ParseTextMarkup(XElement el, AnnotationKind kind)
    {
        if (Page(el) is not { } page || Rect(el) is not { } bounds)
        {
            return null;
        }

        Annotation core = kind switch
        {
            AnnotationKind.Underline => Annotation.Underline(page, bounds, Color(el), Created(el)),
            AnnotationKind.Strikethrough => Annotation.Strikethrough(page, bounds, Color(el), Created(el)),
            _ => Annotation.Highlight(page, bounds, Color(el), Created(el)),
        };
        return WithMetadata(core, el);
    }

    private static Annotation? ParseRectShape(XElement el, AnnotationKind kind)
    {
        if (Page(el) is not { } page || Rect(el) is not { } bounds)
        {
            return null;
        }

        Annotation core = kind == AnnotationKind.Ellipse
            ? Annotation.Ellipse(page, bounds, Color(el), Created(el))
            : Annotation.Rectangle(page, bounds, Color(el), Created(el));
        return WithMetadata(core, el);
    }

    private static Annotation? ParseLine(XElement el)
    {
        if (Page(el) is not { } page
            || ParsePoint(el.Attribute("start")?.Value) is not { } start
            || ParsePoint(el.Attribute("end")?.Value) is not { } end)
        {
            return null;
        }

        // PDF /LE [head tail]; XFDF — отдельные атрибуты head/tail. Arrow ⇔ есть не-None
        // ending хоть на одном конце. Иначе — обычная Line.
        string head = el.Attribute("head")?.Value ?? "None";
        string tail = el.Attribute("tail")?.Value ?? "None";
        bool isArrow = !string.Equals(head, "None", StringComparison.OrdinalIgnoreCase)
                       || !string.Equals(tail, "None", StringComparison.OrdinalIgnoreCase);

        AnnotationPoint[] pts = [start, end];
        Annotation core = isArrow
            ? Annotation.Arrow(page, pts, Color(el), Created(el))
            : Annotation.Line(page, pts, Color(el), Created(el));
        return WithMetadata(core, el);
    }

    private static Annotation? ParseStamp(XElement el)
    {
        if (Page(el) is not { } page || Rect(el) is not { } bounds)
        {
            return null;
        }

        // Stamp factory требует non-blank label — battery пустой <contents> → пропускаем.
        string label = el.Elements().FirstOrDefault(c => c.Name.LocalName == "contents")?.Value ?? string.Empty;
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        return WithMetadata(Annotation.Stamp(page, bounds, label, Color(el), Created(el)), el);
    }

    private static Annotation? ParsePolygon(XElement el)
    {
        if (Page(el) is not { } page)
        {
            return null;
        }

        // <vertices>x1,y1;x2,y2;…</vertices>. Acrobat иногда space-разделяет — допускаем оба.
        string raw = el.Elements().FirstOrDefault(c => c.Name.LocalName == "vertices")?.Value ?? string.Empty;
        var pts = new List<AnnotationPoint>();
        foreach (var pair in raw.Split([';', ' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (ParsePoint(pair) is { } pt)
            {
                pts.Add(pt);
            }
        }

        return pts.Count >= 3 ? WithMetadata(Annotation.Polygon(page, pts, Color(el), Created(el)), el) : null;
    }

    private static Annotation? ParseText(XElement el)
    {
        if (Page(el) is not { } page || Rect(el) is not { } bounds)
        {
            return null;
        }

        string text = el.Elements().FirstOrDefault(c => c.Name.LocalName == "contents")?.Value ?? string.Empty;
        return WithMetadata(Annotation.StickyNote(page, bounds, text, Color(el), Created(el)), el);
    }

    private static Annotation? ParseInk(XElement el)
    {
        if (Page(el) is not { } page)
        {
            return null;
        }

        var points = new List<AnnotationPoint>();
        foreach (var gesture in el.Descendants().Where(d => d.Name.LocalName == "gesture"))
        {
            foreach (var pair in gesture.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (ParsePoint(pair) is { } point)
                {
                    points.Add(point);
                }
            }
        }

        return points.Count >= 2 ? WithMetadata(Annotation.Freehand(page, points, Color(el), Created(el)), el) : null;
    }

    private static Annotation WithMetadata(Annotation core, XElement el) =>
        core with
        {
            ModifiedAt = OptionalDate(el.Attribute("date")?.Value),
            Author = NonEmpty(el.Attribute("name")?.Value),
            Subject = NonEmpty(el.Attribute("subject")?.Value),
        };

    private static string? NonEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private static DateTimeOffset? OptionalDate(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        string s = raw.StartsWith("D:", StringComparison.Ordinal) ? raw[2..] : raw;
        s = s.TrimEnd('Z');
        return DateTimeOffset.TryParseExact(
            s, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset dt)
            ? dt : null;
    }

    private static int? Page(XElement el) =>
        int.TryParse(el.Attribute("page")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int p) && p >= 0
            ? p
            : null;

    private static string Color(XElement el)
    {
        string? c = el.Attribute("color")?.Value;
        return string.IsNullOrWhiteSpace(c) ? "#000000" : c;
    }

    private static DateTimeOffset Created(XElement el) => ParsePdfDate(el.Attribute("creationdate")?.Value);

    private static AnnotationRect? Rect(XElement el)
    {
        string[]? parts = el.Attribute("rect")?.Value.Split(',', StringSplitOptions.TrimEntries);
        if (parts is not { Length: 4 }
            || Number(parts[0]) is not { } x1 || Number(parts[1]) is not { } y1
            || Number(parts[2]) is not { } x2 || Number(parts[3]) is not { } y2)
        {
            return null;
        }

        return new AnnotationRect(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));
    }

    private static AnnotationPoint? ParsePoint(string? pair)
    {
        if (string.IsNullOrWhiteSpace(pair))
        {
            return null;
        }

        string[] xy = pair.Split(',', StringSplitOptions.TrimEntries);
        return xy.Length == 2 && Number(xy[0]) is { } x && Number(xy[1]) is { } y
            ? new AnnotationPoint(x, y)
            : null;
    }

    private static double? Number(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;

    private static DateTimeOffset ParsePdfDate(string? raw)
    {
        // Формат экспортёра: "D:yyyyMMddHHmmssZ". Best-effort — иначе текущее время.
        if (!string.IsNullOrEmpty(raw))
        {
            string s = raw.StartsWith("D:", StringComparison.Ordinal) ? raw[2..] : raw;
            s = s.TrimEnd('Z');
            if (DateTimeOffset.TryParseExact(
                s, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset dt))
            {
                return dt;
            }
        }

        return DateTimeOffset.UtcNow;
    }
}
