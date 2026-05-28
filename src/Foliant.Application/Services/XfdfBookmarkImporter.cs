using System.Globalization;
using System.Xml.Linq;
using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Импорт закладок из XFDF — пара с <see cref="XfdfBookmarkExporter"/>. Совместим с adobe-форматом
/// <c>&lt;bookmark-tree&gt;</c> + nested <c>&lt;bookmark&gt;</c>: глубина восстанавливается из
/// XML-вложенности, страница берётся из атрибута <c>page</c> (0-based) или из
/// <c>&lt;Dest&gt;</c>-child'а (Adobe-style — берём первый числовой токен).
///
/// Битые/неполные узлы пропускаются (best-effort, как у XfdfAnnotationImporter); невалидный XML
/// сверху бросает исключение caller'у.
/// </summary>
public sealed class XfdfBookmarkImporter : IBookmarkImporter
{
    public string FormatName => "XFDF";

    public string FileExtension => "xfdf";

    public IReadOnlyList<Bookmark> Import(string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var doc = XDocument.Parse(content);
        var result = new List<Bookmark>();

        // Adobe spec кладёт закладки внутрь <bookmark-tree>; для robustness берём ВСЕ <bookmark>
        // верхнего уровня независимо от обёртки. Иерархия восстановится из XML-структуры рекурсивно.
        var roots = doc.Descendants()
            .Where(e => e.Name.LocalName == "bookmark"
                && (e.Parent is null
                    || e.Parent.Name.LocalName == "bookmark-tree"
                    || e.Parent.Name.LocalName == "xfdf"))
            .ToList();

        foreach (var root in roots)
        {
            Walk(root, depth: 0, result);
        }

        return result;
    }

    private static void Walk(XElement node, int depth, List<Bookmark> sink)
    {
        if (TryParse(node, depth) is { } bm)
        {
            sink.Add(bm);
        }

        foreach (var child in node.Elements().Where(e => e.Name.LocalName == "bookmark"))
        {
            Walk(child, depth + 1, sink);
        }
    }

    private static Bookmark? TryParse(XElement node, int depth)
    {
        // "Title" — Adobe-капс; для лояльности к рукотворным XFDF принимаем и "title".
        string? title = node.Attribute("Title")?.Value ?? node.Attribute("title")?.Value;
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        int? page = ResolvePage(node);
        if (page is null || page < 0)
        {
            return null;
        }

        DateTimeOffset created = ParsePdfDate(node.Attribute("creationdate")?.Value);
        return new Bookmark(Guid.NewGuid(), page.Value, title.Trim(), created, depth);
    }

    private static int? ResolvePage(XElement node)
    {
        if (int.TryParse(node.Attribute("page")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int p))
        {
            return p;
        }

        // Adobe <Dest> может быть строкой вида "[ 2 /Fit ]" (1-based) — пытаемся вытащить число.
        var dest = node.Elements().FirstOrDefault(e => e.Name.LocalName == "Dest");
        if (dest is not null)
        {
            foreach (var token in dest.Value.Split([' ', '[', ']', '/'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                {
                    // <Dest> страницы 1-based по PDF-спеке.
                    return n - 1;
                }
            }
        }

        return null;
    }

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
