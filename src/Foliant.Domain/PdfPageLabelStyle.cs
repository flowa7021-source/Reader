namespace Foliant.Domain;

/// <summary>
/// Стиль числовой части page-label'а одного диапазона (PDF <c>/PageLabels</c>, ISO 32000-1
/// §12.4.2, ключ <c>/S</c>). Определяет, как номер страницы внутри диапазона превращается в
/// видимую метку («1», «iv», «C-3»).
/// </summary>
public enum PdfPageLabelStyle
{
    /// <summary>Числовой части нет (<c>/S</c> отсутствует): метка состоит только из префикса
    /// (<c>/P</c>). Типично для титульных/разделительных страниц.</summary>
    None,

    /// <summary>Десятичные арабские цифры: 1, 2, 3, … (<c>/S /D</c>).</summary>
    Arabic,

    /// <summary>Заглавные римские цифры: I, II, III, … (<c>/S /R</c>).</summary>
    UpperRoman,

    /// <summary>Строчные римские цифры: i, ii, iii, … (<c>/S /r</c>).</summary>
    LowerRoman,

    /// <summary>Заглавные латинские буквы: A…Z, затем AA…ZZ, AAA… (<c>/S /A</c>).</summary>
    UpperLetters,

    /// <summary>Строчные латинские буквы: a…z, затем aa…zz, aaa… (<c>/S /a</c>).</summary>
    LowerLetters,
}
