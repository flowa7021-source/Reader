using System.Globalization;

namespace Foliant.Domain;

/// <summary>
/// Spec-like описатель «к каким страницам применить операцию», парсится из user-friendly
/// строки вида <c>"1-3,5,7-10"</c> (1-based). Pure-data immutable, без I/O. Используется
/// сервисами PDF-mutate (watermark / header-footer / future crop) для пропуска страниц
/// вне range'а.
///
/// Семантика:
/// <list type="bullet">
/// <item>Номера 1-based в строке-источнике, конвертация в 0-based — в <see cref="Includes"/>.</item>
/// <item>Сегменты — single-number (<c>"5"</c>) или inclusive range (<c>"7-10"</c>),
///   разделены запятой или semicolon. Whitespace игнорируется.</item>
/// <item>Сегменты перекрываются → нормализация (union); порядок не важен.</item>
/// <item><c>null</c> / <see cref="All"/> → применяется ко всем страницам.</item>
/// </list>
/// </summary>
public sealed class PageRange
{
    private readonly IReadOnlyList<(int Start, int End)> _segments; // inclusive, 0-based

    private PageRange(IReadOnlyList<(int Start, int End)> segments)
    {
        _segments = segments;
    }

    /// <summary>Sentinel — применяется к ЛЮБОЙ странице, эквивалентно <c>null</c> в spec'е.</summary>
    public static PageRange All { get; } = new(Array.Empty<(int, int)>());

    /// <summary>True если range охватывает все страницы (пустой sentinel).</summary>
    public bool IsAll => _segments.Count == 0;

    /// <summary>True если страница (0-based) попадает хотя бы в один сегмент range'а.
    /// Для <see cref="All"/> всегда true.</summary>
    public bool Includes(int pageIndex0Based)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex0Based);
        if (IsAll)
        {
            return true;
        }
        foreach (var (start, end) in _segments)
        {
            if (pageIndex0Based >= start && pageIndex0Based <= end)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Парсит строку формата <c>"1-3,5,7-10"</c>. Whitespace игнорируется; запятая
    /// и semicolon — оба разделители. Пустая/whitespace строка → <see cref="All"/>.
    /// Невалидный токен (нечисловое, отрицательное, end &lt; start) → <see cref="FormatException"/>.</summary>
    public static PageRange Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return All;
        }

        var segments = new List<(int, int)>();
        foreach (string raw in input.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            string token = raw.Trim();
            if (token.Length == 0)
            {
                continue;
            }

            int dash = token.IndexOf('-', StringComparison.Ordinal);
            if (dash < 0)
            {
                int n = ParseOneBased(token);
                segments.Add((n - 1, n - 1));
            }
            else
            {
                string leftStr = token[..dash].Trim();
                string rightStr = token[(dash + 1)..].Trim();
                int start = ParseOneBased(leftStr);
                int end = ParseOneBased(rightStr);
                if (end < start)
                {
                    throw new FormatException($"Range '{token}' has end < start.");
                }
                segments.Add((start - 1, end - 1));
            }
        }

        return new PageRange(segments);
    }

    /// <summary>Try-парсинг: <c>true</c> + range на успехе, <c>false</c> + <c>null</c> на сбое.
    /// Удобно для UI-validation без try/catch.</summary>
    public static bool TryParse(string? input, out PageRange? range)
    {
        try
        {
            range = Parse(input);
            return true;
        }
        catch (FormatException)
        {
            range = null;
            return false;
        }
    }

    private static int ParseOneBased(string token)
    {
        if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 1)
        {
            throw new FormatException($"Invalid 1-based page number '{token}'.");
        }
        return n;
    }
}
