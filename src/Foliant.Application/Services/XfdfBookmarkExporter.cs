using System.Globalization;
using System.Xml.Linq;
using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Экспорт закладок в XFDF — Adobe-совместимый XML-контейнер. Использует адобовский
/// <c>&lt;bookmark-tree&gt;</c> + nested <c>&lt;bookmark&gt;</c>: глубина передаётся через
/// вложенность XML, страница — атрибутом <c>page</c> (0-based, как в нашем домене), название —
/// атрибутом <c>Title</c> (Adobe). Stateless, без I/O.
///
/// Round-trip — пара с <see cref="XfdfBookmarkImporter"/>. Markdown-экспортер уже передаёт
/// иерархию через отступ, JSON — через поле <c>Depth</c>; XFDF — через структуру XML.
/// </summary>
public sealed class XfdfBookmarkExporter : IBookmarkExporter
{
    private static readonly XNamespace Ns = "http://ns.adobe.com/xfdf/";

    public string FormatName => "XFDF";

    public string FileExtension => "xfdf";

    public string Export(IReadOnlyList<Bookmark> bookmarks)
    {
        ArgumentNullException.ThrowIfNull(bookmarks);

        var tree = new XElement(Ns + "bookmark-tree");
        // Когда хотя бы у одной закладки depth > 0, считаем коллекцию иерархической и сохраняем
        // DOM-порядок (depth-first, как из reader'а). Иначе — sort by PageIndex для стабильности
        // (как Markdown-экспортер). Это совпадение поведения важно для UX: один и тот же набор
        // bookmark'ов даёт одинаковый порядок в Markdown и XFDF.
        IReadOnlyList<Bookmark> ordered = bookmarks.Any(b => b.Depth > 0)
            ? [.. bookmarks]
            : [.. bookmarks.OrderBy(b => b.PageIndex)];

        BuildTree(tree, ordered, startIndex: 0, parentDepth: -1);

        var root = new XElement(
            Ns + "xfdf",
            new XAttribute(XNamespace.Xml + "space", "preserve"),
            tree);

        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Environment.NewLine + root;
    }

    /// <summary>Рекурсивно собираем дерево из flat-списка по правилу: каждый узел с depth = N
    /// родителю (depth = N-1) получает в children всех последующих узлов с depth &gt; N до
    /// первого узла с depth ≤ N. Возвращает индекс «следующий после поддерева».</summary>
    private static int BuildTree(XElement parent, IReadOnlyList<Bookmark> items, int startIndex, int parentDepth)
    {
        int i = startIndex;
        while (i < items.Count)
        {
            var bm = items[i];
            if (bm.Depth <= parentDepth)
            {
                break;
            }

            var element = new XElement(
                Ns + "bookmark",
                new XAttribute("Title", bm.Label),
                new XAttribute("page", bm.PageIndex.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("creationdate", "D:" + bm.CreatedAt.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "Z"));
            parent.Add(element);

            // Children — узлы со строго большим depth.
            i = BuildTree(element, items, i + 1, bm.Depth);
        }

        return i;
    }
}
