namespace Foliant.Domain;

/// <summary>
/// Один шрифт, используемый в PDF (font resource, ISO 32000-1 §9.5–§9.6): запись из словаря
/// страницы <c>/Resources → /Font</c>. В UI показывается как строка списка «Acrobat Document
/// Properties → Fonts» — имя шрифта, его тип и признак встраивания. Один и тот же шрифт может
/// фигурировать на многих страницах; в списке документа дубликаты схлопываются.
///
/// <para>Pure-data, immutable. <c>with</c>-копии идут мимо какой-либо валидации — это допустимо, как и
/// у других domain-record'ов.</para>
/// </summary>
/// <param name="Name">Имя шрифта (<c>/BaseFont</c>), например <c>Helvetica</c> или
/// <c>ABCDEF+Arial</c>. Subset-префикс (<c>ABCDEF+</c>) сохраняется как есть. Для шрифтов без
/// <c>/BaseFont</c> — пустая строка.</param>
/// <param name="Subtype">Сырое имя <c>/Subtype</c> словаря шрифта без ведущего слэша (например
/// <c>Type1</c>, <c>TrueType</c>, <c>Type0</c>, <c>Type3</c>).</param>
/// <param name="IsEmbedded"><see langword="true"/>, если глифы шрифта встроены в файл —
/// <c>/FontDescriptor</c> содержит <c>/FontFile</c>, <c>/FontFile2</c> или <c>/FontFile3</c> (для
/// <c>/Type0</c> дескриптор берётся из <c>/DescendantFonts[0]</c>). 14 стандартных шрифтов обычно не
/// встраиваются.</param>
public sealed record PdfFontInfo(string Name, string Subtype, bool IsEmbedded);
