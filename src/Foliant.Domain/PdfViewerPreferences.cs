namespace Foliant.Domain;

/// <summary>
/// Настройки начального вида PDF («Initial View» в Acrobat, ISO 32000-1 §7.7.2 / §12.2): раскладка
/// страниц, режим панелей и набор UI-флагов, которые viewer применяет при открытии документа.
/// <see cref="PageLayout"/> и <see cref="PageMode"/> — ключи catalog'а (<c>/PageLayout</c>,
/// <c>/PageMode</c>); пять булевых флагов живут в подсловаре catalog'а <c>/ViewerPreferences</c>.
///
/// <para>Pure-data, immutable. Значения по умолчанию (PDF-default «как нет ключа») собраны в
/// <see cref="Default"/>. <c>with</c>-копии идут мимо какой-либо валидации — это допустимо, как и у
/// других domain-record'ов.</para>
/// </summary>
/// <param name="PageLayout">Раскладка страниц (<c>/PageLayout</c>).</param>
/// <param name="PageMode">Режим панелей / показа (<c>/PageMode</c>).</param>
/// <param name="HideToolbar">Скрыть панель инструментов viewer'а (<c>/HideToolbar</c>).</param>
/// <param name="HideMenubar">Скрыть строку меню viewer'а (<c>/HideMenubar</c>).</param>
/// <param name="FitWindow">Подогнать размер окна под первую страницу (<c>/FitWindow</c>).</param>
/// <param name="CenterWindow">Центрировать окно на экране (<c>/CenterWindow</c>).</param>
/// <param name="DisplayDocTitle">Показывать в заголовке окна <c>/Title</c> из метаданных вместо
/// имени файла (<c>/DisplayDocTitle</c>).</param>
public sealed record PdfViewerPreferences(
    PdfPageLayout PageLayout,
    PdfPageMode PageMode,
    bool HideToolbar,
    bool HideMenubar,
    bool FitWindow,
    bool CenterWindow,
    bool DisplayDocTitle)
{
    /// <summary>Значения по умолчанию: раскладка и режим = <see cref="PdfPageLayout.Default"/> /
    /// <see cref="PdfPageMode.Default"/> (ключи отсутствуют), все флаги = <see langword="false"/>
    /// (PDF-default). Возвращается при чтении документа без <c>/ViewerPreferences</c>.</summary>
    public static readonly PdfViewerPreferences Default =
        new(PdfPageLayout.Default, PdfPageMode.Default, false, false, false, false, false);
}
