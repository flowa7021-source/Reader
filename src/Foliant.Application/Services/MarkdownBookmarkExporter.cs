using System.Globalization;
using System.Text;
using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Markdown-экспорт закладок: список вида «- Page N — Label». <see cref="Bookmark.Depth"/>
/// рисуется двумя пробелами на уровень — Markdown-парсеры превращают это в вложенный bullet.
/// Empty input → заголовок + плейсхолдер.
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

        // Для импорта TOC сохраняем DOM-порядок (depth-first из PdfPigOutlineReader).
        // Сортировка по PageIndex сломала бы иерархию — depth-2 узел мог бы оказаться раньше
        // depth-1 родителя при out-of-order pages. Считаем коллекцию иерархической, если хотя
        // бы у одной закладки depth > 0; иначе fallback на старое поведение (sort by PageIndex).
        IEnumerable<Bookmark> ordered = bookmarks.Any(b => b.Depth > 0)
            ? bookmarks
            : bookmarks.OrderBy(b => b.PageIndex);

        foreach (var bm in ordered)
        {
            int depth = Math.Max(0, bm.Depth);
            sb.Append(' ', depth * 2);
            sb.Append("- Page ");
            sb.Append((bm.PageIndex + 1).ToString(CultureInfo.InvariantCulture));
            sb.Append(" — ");
            sb.AppendLine(bm.Label);
        }

        return sb.ToString();
    }
}
