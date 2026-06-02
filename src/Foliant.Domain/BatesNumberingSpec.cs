using System.Globalization;

namespace Foliant.Domain;

/// <summary>
/// Описание Bates-нумерации PDF — монотонного юридического счётчика страниц (паритет с
/// Acrobat Pro «Bates Numbering»; Q-F для юристов/нотариусов). В отличие от
/// <see cref="HeaderFooterSpec"/> здесь нет произвольного текста и placeholder'ов: только
/// последовательный номер с фиксированными <see cref="Prefix"/>/<see cref="Suffix"/>,
/// zero-padding (<see cref="Digits"/>) и настраиваемым стартом (<see cref="StartNumber"/>),
/// наносимый в один угол страницы. Pure-data immutable, без I/O.
///
/// <para>Счётчик идёт по ВСЕМ страницам документа: для страницы с 0-based индексом
/// <c>pageIndex</c> число = <c>StartNumber + pageIndex</c>. Опциональный
/// <see cref="Range"/> лишь ограничивает, на какие страницы НАНОСИТСЯ штамп — он не
/// сдвигает счётчик, поэтому при печати поддиапазона Bates-номера остаются стабильными
/// (страница 5 всегда «…05» независимо от того, печатали ли мы страницы 1–4).</para>
/// </summary>
/// <param name="Prefix">Неизменный префикс перед номером, напр. <c>"ACME-"</c>. Может быть пустым.</param>
/// <param name="Suffix">Неизменный суффикс после номера. Может быть пустым.</param>
/// <param name="StartNumber">Номер, присваиваемый ПЕРВОЙ странице документа (1-based значение,
/// которое видит пользователь; типично <c>1</c>). Применяется к нулевой странице.</param>
/// <param name="Digits">Ширина zero-padding: <c>6</c> → <c>000001</c>. Минимум <c>1</c>; если
/// число длиннее, оно не обрезается.</param>
/// <param name="Position">Угол страницы для штампа (см. <see cref="BatesPosition"/>).</param>
/// <param name="FontSize">Размер шрифта в PDF points. Типично 8–10 для Bates-штампа.</param>
/// <param name="R">Red-канал (0..255).</param>
/// <param name="G">Green-канал (0..255).</param>
/// <param name="B">Blue-канал (0..255).</param>
/// <param name="Range">К каким страницам наносить штамп; <c>null</c> — ко всем. Не влияет на
/// значение счётчика (см. summary).</param>
public sealed record BatesNumberingSpec(
    string Prefix,
    string Suffix,
    int StartNumber,
    int Digits,
    BatesPosition Position,
    double FontSize,
    byte R,
    byte G,
    byte B,
    PageRange? Range = null)
{
    /// <summary>Формирует Bates-текст для страницы с заданным 0-based индексом:
    /// <c>{Prefix}{(StartNumber + pageIndex):D{Digits}}{Suffix}</c>. Число форматируется
    /// инвариантно (без разделителей разрядов, ASCII-цифры). Public для unit-тестирования
    /// без нативного PDFium.</summary>
    public string FormatFor(int pageIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        int n = StartNumber + pageIndex;
        // "D" + Digits — динамическая ширина zero-pad; InvariantCulture фиксирует ASCII-цифры
        // независимо от локали ОС (юридический штамп должен быть детерминированным).
        string number = n.ToString("D" + Digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return string.Concat(Prefix, number, Suffix);
    }
}

/// <summary>Угол страницы для Bates-штампа. Bates-номера по конвенции ставят внизу
/// (footer-зона), поэтому только нижние позиции — этого достаточно для паритета с Acrobat.</summary>
public enum BatesPosition
{
    BottomLeft,
    BottomCenter,
    BottomRight,
}
