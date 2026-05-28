namespace Foliant.Domain;

/// <summary>
/// Описание колонтитулов PDF — заголовок (header) и подвал (footer). Любой из двух может быть
/// <c>null</c>/whitespace — тогда соответствующая полоса не рисуется. Pure-data; placeholder'ы
/// в строках расширяются сервисом наложения per-page:
/// <list type="bullet">
/// <item><c>{page}</c> — 1-based номер страницы.</item>
/// <item><c>{total}</c> — общее число страниц.</item>
/// <item><c>{filename}</c> — basename исходного файла.</item>
/// <item><c>{date}</c> — текущая дата в формате <c>yyyy-MM-dd</c>.</item>
/// </list>
/// Так покрывается Q-F14 «текст / дата / имя файла / страница X из Y».
/// </summary>
/// <param name="HeaderText">Текст верхнего колонтитула или <c>null</c>.</param>
/// <param name="FooterText">Текст нижнего колонтитула или <c>null</c>.</param>
/// <param name="FontSize">Размер шрифта в PDF points. Типично 9–11 для документов.</param>
/// <param name="R">Red-канал (0..255).</param>
/// <param name="G">Green-канал (0..255).</param>
/// <param name="B">Blue-канал (0..255).</param>
public sealed record HeaderFooterSpec(
    string? HeaderText,
    string? FooterText,
    double FontSize,
    byte R,
    byte G,
    byte B);
