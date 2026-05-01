using System.Globalization;
using System.Text;
using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Markdown-экспорт закладок: список вида «- Page N — Label» в порядке
/// возрастания страниц. Empty input → заголовок + плейсхолдер.
/// </summary>
public sealed class MarkdownBookmarkExporter : IBookmarkExporter
{
    public string FormatName => "Markdown";

    public string FileExtension => "md";

    public string Export(IReadOnlyList<Bookmark> bookmarks)
    {
        ArgumentNullException.ThrowIfNull(bookmarks);

        var sb = new StringBuilder();
        sb.AppendLine("# Bookmarks");
        sb.AppendLine();

        if (bookmarks.Count == 0)
        {
            sb.AppendLine("_No bookmarks._");
            return sb.ToString();
        }

        foreach (var bm in bookmarks.OrderBy(b => b.PageIndex))
        {
            sb.Append("- Page ");
            sb.Append((bm.PageIndex + 1).ToString(CultureInfo.InvariantCulture));
            sb.Append(" — ");
            sb.AppendLine(bm.Label);
        }

        return sb.ToString();
    }
}
