namespace Foliant.Domain;

/// <summary>
/// Один встроенный файл-вложение PDF (embedded file attachment, ISO 32000-1 §7.11): запись в
/// catalog name-tree <c>/Names → /EmbeddedFiles</c>, состоящая из file-specification словаря
/// (<c>/Type /Filespec</c>) и связанного embedded-file потока (<c>/Type /EmbeddedFile</c>). В UI
/// показывается как прикреплённый к документу файл, который можно извлечь или удалить.
///
/// <para>Pure-data, immutable. <c>with</c>-копии идут мимо какой-либо валидации — это допустимо, как и
/// у других domain-record'ов.</para>
/// </summary>
/// <param name="Name">Имя файла вложения (<c>/F</c> / <c>/UF</c>, оно же ключ name-tree). Уникально в
/// пределах документа; именно по нему вложение извлекается / удаляется.</param>
/// <param name="Size">Размер вложения в байтах после декодирования (длина исходного файла, а не
/// сжатого потока).</param>
/// <param name="Description">Необязательное описание (<c>/Desc</c>) или <see langword="null"/>, если
/// описания нет.</param>
public sealed record PdfAttachment(string Name, long Size, string? Description);
