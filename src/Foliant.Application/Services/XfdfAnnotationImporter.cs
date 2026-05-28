using System.Globalization;
using System.Xml.Linq;
using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Импорт аннотаций из XFDF (Q-F17) — обратная операция к <see cref="XfdfAnnotationExporter"/>.
/// Элементы ищутся по локальному имени (highlight / text / ink) независимо от namespace, чтобы
/// принимать XFDF из Acrobat и др. инструментов. Битые/неполные элементы пропускаются;
/// невалидный XML бросает исключение вызывающему.
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
                "highlight" => ParseHighlight(el),
                "text" => ParseText(el),
                "ink" => ParseInk(el),
                _ => null,
            };

            if (annotation is not null)
            {
                result.Add(annotation);
            }
        }

        return result;
    }

    private static Annotation? ParseHighlight(XElement el)
    {
        if (Page(el) is not { } page || Rect(el) is not { } bounds)
        {
            return null;
        }

        return WithMetadata(Annotation.Highlight(page, bounds, Color(el), Created(el)), el);
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

    private static AnnotationPoint? ParsePoint(string pair)
    {
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
