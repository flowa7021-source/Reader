using System.Globalization;
using System.Text;

namespace Foliant.Domain;

/// <summary>
/// Превращает набор <see cref="PdfPageLabelRange"/> в видимую метку страницы (PDF <c>/PageLabels</c>,
/// ISO 32000-1 §12.4.2): выбирает диапазон, накрывающий страницу, и форматирует
/// <c>Prefix + numeric(Start + offset, Style)</c>. Pure-domain, культур-независимо.
/// </summary>
public static class PdfPageLabelFormatter
{
    /// <summary>
    /// Формирует метку для страницы <paramref name="pageIndex"/> (0-based) по набору диапазонов.
    /// Если ни один диапазон не накрывает страницу (нет диапазона с
    /// <see cref="PdfPageLabelRange.StartPageIndex"/> ≤ <paramref name="pageIndex"/>), возвращает
    /// пустую строку — caller волен подставить десятичный <c>(pageIndex + 1)</c>.
    /// </summary>
    /// <param name="ranges">Диапазоны нумерации (порядок не важен; выбирается ближайший слева).</param>
    /// <param name="pageIndex">0-based индекс страницы.</param>
    /// <returns>Видимая метка или пустая строка, если применимого диапазона нет.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="ranges"/> = <see langword="null"/>.</exception>
    /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="pageIndex"/> &lt; 0.</exception>
    public static string Format(IReadOnlyList<PdfPageLabelRange> ranges, int pageIndex)
    {
        System.ArgumentNullException.ThrowIfNull(ranges);
        System.ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);

        PdfPageLabelRange? range = SelectRange(ranges, pageIndex);
        if (range is null)
        {
            return string.Empty;
        }

        string prefix = range.Prefix ?? string.Empty;
        if (range.Style == PdfPageLabelStyle.None)
        {
            return prefix;
        }

        int value = range.Start + (pageIndex - range.StartPageIndex);
        return prefix + FormatNumber(value, range.Style);
    }

    /// <summary>
    /// Форматирует одно числовое значение в выбранном стиле (без префикса).
    /// <see cref="PdfPageLabelStyle.None"/> → пустая строка.
    /// </summary>
    /// <param name="value">Число к форматированию (≥ 1 для всех стилей кроме
    /// <see cref="PdfPageLabelStyle.None"/>).</param>
    /// <param name="style">Стиль нумерации.</param>
    /// <returns>Числовая часть метки.</returns>
    /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="style"/> вне enum'а, либо
    /// <paramref name="value"/> &lt; 1 для не-<see cref="PdfPageLabelStyle.None"/> стиля.</exception>
    public static string FormatNumber(int value, PdfPageLabelStyle style)
    {
        if (style == PdfPageLabelStyle.None)
        {
            return string.Empty;
        }

        if (!System.Enum.IsDefined(style))
        {
            throw new System.ArgumentOutOfRangeException(nameof(style), style, "Unknown page-label style.");
        }

        System.ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);

        return style switch
        {
            PdfPageLabelStyle.Arabic => value.ToString(CultureInfo.InvariantCulture),
            PdfPageLabelStyle.UpperRoman => ToRoman(value, upper: true),
            PdfPageLabelStyle.LowerRoman => ToRoman(value, upper: false),
            PdfPageLabelStyle.UpperLetters => ToLetters(value, upper: true),
            PdfPageLabelStyle.LowerLetters => ToLetters(value, upper: false),
            _ => string.Empty,
        };
    }

    private static PdfPageLabelRange? SelectRange(IReadOnlyList<PdfPageLabelRange> ranges, int pageIndex)
    {
        PdfPageLabelRange? best = null;
        foreach (PdfPageLabelRange r in ranges)
        {
            if (r is null)
            {
                continue;
            }

            if (r.StartPageIndex <= pageIndex && (best is null || r.StartPageIndex > best.StartPageIndex))
            {
                best = r;
            }
        }

        return best;
    }

    private static readonly (int Value, string Upper, string Lower)[] RomanUnits =
    [
        (1000, "M", "m"), (900, "CM", "cm"), (500, "D", "d"), (400, "CD", "cd"),
        (100, "C", "c"), (90, "XC", "xc"), (50, "L", "l"), (40, "XL", "xl"),
        (10, "X", "x"), (9, "IX", "ix"), (5, "V", "v"), (4, "IV", "iv"), (1, "I", "i"),
    ];

    private static string ToRoman(int value, bool upper)
    {
        var sb = new StringBuilder();
        int remaining = value;
        foreach ((int unit, string up, string low) in RomanUnits)
        {
            while (remaining >= unit)
            {
                sb.Append(upper ? up : low);
                remaining -= unit;
            }
        }

        return sb.ToString();
    }

    private static string ToLetters(int value, bool upper)
    {
        // ISO 32000-1 §12.4.2: A…Z для первых 26 страниц, AA…ZZ для следующих 26, и т.д.
        char baseChar = upper ? 'A' : 'a';
        int zeroBased = value - 1;
        char letter = (char)(baseChar + (zeroBased % 26));
        int count = (zeroBased / 26) + 1;
        return new string(letter, count);
    }
}
