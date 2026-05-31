using System.Net;
using System.Text;

namespace Foliant.Engines.Mobi;

/// <summary>
/// Минимальный HTML→plain-text стриппер для MOBI-текста (MOBI хранит подмножество HTML).
/// Удаляет теги, декодирует HTML-entity, схлопывает пробелы. Block-level теги (p/div/br/h*)
/// заменяются пробелом, чтобы слова не слипались. Без сторонних зависимостей.
/// </summary>
internal static class MobiHtml
{
    public static string StripToText(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(html.Length);
        bool inTag = false;
        foreach (char ch in html)
        {
            if (ch == '<')
            {
                inTag = true;
                sb.Append(' '); // граница тега → пробел, чтобы слова не слипались
            }
            else if (ch == '>')
            {
                inTag = false;
            }
            else if (!inTag)
            {
                sb.Append(ch);
            }
        }

        string decoded = WebUtility.HtmlDecode(sb.ToString());
        return CollapseWhitespace(decoded);
    }

    internal static string CollapseWhitespace(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool prevWs = false;
        foreach (char ch in text)
        {
            if (char.IsWhiteSpace(ch) || ch == '\0')
            {
                if (!prevWs)
                {
                    sb.Append(' ');
                    prevWs = true;
                }
            }
            else
            {
                sb.Append(ch);
                prevWs = false;
            }
        }

        return sb.ToString().Trim();
    }
}
