namespace Foliant.Domain;

/// <summary>
/// Раскладка страниц при открытии документа (PDF catalog <c>/PageLayout</c>, ISO 32000-1 §7.7.2,
/// «Initial View» в Acrobat). Определяет, как viewer располагает страницы при первом показе
/// (одна страница, непрерывная колонка, разворот).
/// </summary>
public enum PdfPageLayout
{
    /// <summary>Ключ <c>/PageLayout</c> отсутствует — viewer использует своё значение по умолчанию
    /// (как правило <see cref="SinglePage"/>).</summary>
    Default,

    /// <summary>Одна страница за раз (<c>/SinglePage</c>).</summary>
    SinglePage,

    /// <summary>Непрерывная одна колонка (<c>/OneColumn</c>).</summary>
    OneColumn,

    /// <summary>Непрерывный разворот в две колонки, нечётные страницы слева (<c>/TwoColumnLeft</c>).</summary>
    TwoColumnLeft,

    /// <summary>Непрерывный разворот в две колонки, нечётные страницы справа (<c>/TwoColumnRight</c>).</summary>
    TwoColumnRight,

    /// <summary>Разворот по две страницы, нечётные страницы слева (<c>/TwoPageLeft</c>).</summary>
    TwoPageLeft,

    /// <summary>Разворот по две страницы, нечётные страницы справа (<c>/TwoPageRight</c>).</summary>
    TwoPageRight,
}
