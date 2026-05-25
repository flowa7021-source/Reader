namespace Foliant.Application.Services;

/// <summary>Сегмент сниппета: либо обычный текст, либо текст-совпадение, который UI рендерит
/// с подсветкой (yellow background и т.п.). <c>IsMatch</c> — единственный признак.</summary>
public readonly record struct SnippetSegment(string Text, bool IsMatch);

/// <summary>
/// Stateless утилита: разбивает <c>snippet</c> на список <see cref="SnippetSegment"/>,
/// помечая фрагменты, совпадающие с <c>match</c>. Используется в search-sidebar для
/// рендера хитов с подсвеченным словом запроса.
///
/// По умолчанию совпадение регистро-нечувствительное (<c>matchCase=false</c>);
/// разделители Unicode не учитываются — это substring-match как и в
/// <see cref="ISearchService"/>. Empty/whitespace <c>match</c> → весь сниппет
/// возвращается как один не-match сегмент (предотвращает бесконечный цикл).
/// </summary>
public static class SnippetHighlighter
{
    public static IReadOnlyList<SnippetSegment> Highlight(
        string snippet,
        string match,
        bool matchCase = false)
    {
        ArgumentNullException.ThrowIfNull(snippet);
        ArgumentNullException.ThrowIfNull(match);

        if (snippet.Length == 0)
        {
            return [];
        }

        if (string.IsNullOrEmpty(match))
        {
            return [new SnippetSegment(snippet, false)];
        }

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var result = new List<SnippetSegment>();
        int cursor = 0;

        while (cursor < snippet.Length)
        {
            int idx = snippet.IndexOf(match, cursor, comparison);
            if (idx < 0)
            {
                result.Add(new SnippetSegment(snippet[cursor..], false));
                break;
            }

            if (idx > cursor)
            {
                result.Add(new SnippetSegment(snippet[cursor..idx], false));
            }

            // Берём именно подстроку из snippet (а не из match), чтобы сохранить
            // оригинальный регистр совпадения для рендера.
            result.Add(new SnippetSegment(snippet.Substring(idx, match.Length), true));
            cursor = idx + match.Length;
        }

        return result;
    }
}
