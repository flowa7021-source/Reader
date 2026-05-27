using System.Globalization;
using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Слияние импортированных закладок (Q-F4 sidecar) с уже существующими без дублей. Параллель к
/// <see cref="AnnotationMerge"/>: импорт генерирует новые <see cref="Bookmark.Id"/>, поэтому
/// совпадение определяется по содержимому (страница + метка), а не по Id. <see cref="Bookmark.CreatedAt"/>
/// в ключ НЕ входит — повторный импорт одного файла обязан быть идемпотентным. Чистая функция.
/// </summary>
public static class BookmarkMerge
{
    /// <summary>Подмножество <paramref name="incoming"/>, которого ещё нет в
    /// <paramref name="existing"/> (по странице + метке). Дубли внутри <paramref name="incoming"/>
    /// тоже схлопываются. Порядок исходного <paramref name="incoming"/> сохраняется.</summary>
    public static IReadOnlyList<Bookmark> NewBookmarks(
        IReadOnlyList<Bookmark> existing,
        IReadOnlyList<Bookmark> incoming)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(incoming);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var b in existing)
        {
            seen.Add(Signature(b));
        }

        var result = new List<Bookmark>();
        foreach (var b in incoming)
        {
            if (seen.Add(Signature(b)))
            {
                result.Add(b);
            }
        }

        return result;
    }

    private static string Signature(Bookmark b) =>
        string.Concat(b.PageIndex.ToString(CultureInfo.InvariantCulture), "|", b.Label);
}
