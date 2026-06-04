namespace Foliant.Domain;

/// <summary>
/// Описание правки classic PDF <c>/Info</c>-метаданных (Document Properties в Acrobat).
/// Pure-data, без I/O — потребляется <c>IPdfMetadataEditService</c>, который ре-сериализует
/// документ с новым <c>/Info</c>.
///
/// Семантика каждого поля:
/// <list type="bullet">
/// <item><c>null</c> (значение по умолчанию) → <b>не менять</b>: сохранить текущее значение
/// исходного документа.</item>
/// <item>Пустая строка <c>""</c> → <b>очистить</b>: записать пустое значение в <c>/Info</c>.</item>
/// <item>Непустая строка → записать как есть (строки культур-независимы, без транслитерации).</item>
/// </list>
///
/// Scope — только classic <c>/Info</c> dictionary. XMP metadata stream, custom-properties и
/// <c>/CreationDate</c>/<c>/ModDate</c> вне области (см. PR-описание).
/// </summary>
/// <param name="Title">Заголовок документа (<c>/Title</c>).</param>
/// <param name="Author">Автор (<c>/Author</c>).</param>
/// <param name="Subject">Тема (<c>/Subject</c>).</param>
/// <param name="Keywords">Ключевые слова (<c>/Keywords</c>); формат строки на усмотрение caller'а.</param>
/// <param name="Creator">Приложение-создатель исходного контента (<c>/Creator</c>).</param>
/// <param name="Producer">Приложение-конвертер в PDF (<c>/Producer</c>).</param>
public sealed record PdfMetadataSpec(
    string? Title = null,
    string? Author = null,
    string? Subject = null,
    string? Keywords = null,
    string? Creator = null,
    string? Producer = null);
