namespace Foliant.Domain;

/// <summary>
/// Описание колонтитулов PDF — список «полос» (<see cref="HeaderFooterBand"/>), каждая
/// в одной из шести позиций (<see cref="HeaderFooterPosition"/>), плюс общий font-size,
/// цвет и опциональный <see cref="PageRange"/>. Pure-data; placeholder'ы в строках
/// расширяются сервисом наложения per-page:
/// <list type="bullet">
/// <item><c>{page}</c> — 1-based номер страницы.</item>
/// <item><c>{total}</c> — общее число страниц.</item>
/// <item><c>{filename}</c> — basename исходного файла.</item>
/// <item><c>{date}</c> — текущая дата в формате <c>yyyy-MM-dd</c>.</item>
/// </list>
/// Так покрывается Q-F14 «текст / дата / имя файла / страница X из Y» + «все позиции» +
/// «по диапазону».
/// </summary>
/// <param name="Bands">Полосы колонтитулов. Каждая позиция может встречаться максимум один раз;
/// порядок не важен. Пустой список = no-op (сервис ничего не наносит).</param>
/// <param name="FontSize">Размер шрифта в PDF points. Типично 9–11 для документов.</param>
/// <param name="R">Red-канал (0..255).</param>
/// <param name="G">Green-канал (0..255).</param>
/// <param name="B">Blue-канал (0..255).</param>
/// <param name="Range">К каким страницам применить header/footer; <c>null</c> — ко всем
/// (Q-F14 «по диапазону»).</param>
public sealed record HeaderFooterSpec(
    IReadOnlyList<HeaderFooterBand> Bands,
    double FontSize,
    byte R,
    byte G,
    byte B,
    PageRange? Range = null)
{
    /// <summary>Convenience-factory для legacy-вызовов: формирует spec с одной центральной
    /// верхней полосой (Header) и одной центральной нижней (Footer). Пустая / null строка
    /// для соответствующей полосы — она не добавляется. Это позволяет существующим тестам
    /// и call site'ам переключиться на новую форму с минимальной правкой.</summary>
    public static HeaderFooterSpec FromCenterTexts(
        string? headerText,
        string? footerText,
        double fontSize,
        byte r,
        byte g,
        byte b,
        PageRange? range = null)
    {
        var bands = new List<HeaderFooterBand>(2);
        if (!string.IsNullOrWhiteSpace(headerText))
        {
            bands.Add(new HeaderFooterBand(HeaderFooterPosition.TopCenter, headerText));
        }
        if (!string.IsNullOrWhiteSpace(footerText))
        {
            bands.Add(new HeaderFooterBand(HeaderFooterPosition.BottomCenter, footerText));
        }
        return new HeaderFooterSpec(bands, fontSize, r, g, b, range);
    }
}

/// <summary>Одна «полоса» колонтитула: позиция (один из шести углов/центров) + текст
/// (с возможными placeholder'ами).</summary>
/// <param name="Position">Куда наносить текст (см. <see cref="HeaderFooterPosition"/>).</param>
/// <param name="Text">Сам текст (placeholder'ы расширяются сервисом).</param>
public sealed record HeaderFooterBand(HeaderFooterPosition Position, string Text);

/// <summary>Шесть стандартных позиций колонтитула на странице. Покрывает Q-F14 «все позиции».</summary>
public enum HeaderFooterPosition
{
    TopLeft,
    TopCenter,
    TopRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
}
