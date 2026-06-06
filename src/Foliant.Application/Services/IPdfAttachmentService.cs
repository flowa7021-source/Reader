using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Управление встроенными файл-вложениями PDF (embedded file attachments, «Attachments» в Acrobat,
/// ISO 32000-1 §7.11): список / извлечение / добавление / удаление файлов, хранящихся в catalog
/// name-tree <c>/Names → /EmbeddedFiles</c>. Симметричный read/write контракт, как у page-label и
/// OCG-сервисов.
///
/// <para>Stateless; запись не мутирует оригинал — результат пишется в <c>targetPath</c> атомарно
/// (temp + Move) через инкрементальный апдейт (ISO 32000-1 §7.5.6), поэтому <c>sourcePath</c> может
/// совпадать с <c>targetPath</c>.</para>
/// </summary>
public interface IPdfAttachmentService
{
    /// <summary>
    /// Читает список вложений из <paramref name="pdfPath"/>. Документ без <c>/EmbeddedFiles</c> (или
    /// повреждённый) → пустой список (best-effort, без throw'а). Результат отсортирован по
    /// <see cref="PdfAttachment.Name"/> (ordinal).
    /// </summary>
    /// <param name="pdfPath">Путь к PDF.</param>
    /// <param name="ct">Отмена.</param>
    /// <returns>Вложения документа (возможно пустой список).</returns>
    /// <exception cref="System.ArgumentException"><paramref name="pdfPath"/> пуст / whitespace.</exception>
    Task<IReadOnlyList<PdfAttachment>> ListAsync(string pdfPath, CancellationToken ct);

    /// <summary>
    /// Извлекает вложение с именем <paramref name="attachmentName"/> и записывает его декодированные
    /// байты в <paramref name="targetFilePath"/> (атомарно: temp + Move).
    /// </summary>
    /// <param name="pdfPath">Путь к PDF с вложением.</param>
    /// <param name="attachmentName">Имя вложения (<see cref="PdfAttachment.Name"/>).</param>
    /// <param name="targetFilePath">Куда записать извлечённые байты.</param>
    /// <param name="ct">Отмена.</param>
    /// <exception cref="System.ArgumentException"><paramref name="pdfPath"/>,
    /// <paramref name="attachmentName"/> либо <paramref name="targetFilePath"/> пуст / whitespace.</exception>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">В документе нет вложения с
    /// именем <paramref name="attachmentName"/>.</exception>
    Task ExtractAsync(string pdfPath, string attachmentName, string targetFilePath, CancellationToken ct);

    /// <summary>
    /// Встраивает байты файла <paramref name="fileToAttachPath"/> в копию PDF под его именем
    /// (<see cref="System.IO.Path.GetFileName(string)"/>). Если вложение с таким именем уже есть — оно
    /// заменяется новыми байтами / описанием. Существующие вложения с другими именами сохраняются.
    /// </summary>
    /// <param name="sourcePath">Исходный PDF; не модифицируется.</param>
    /// <param name="targetPath">Куда записать результат. Может совпадать с <paramref name="sourcePath"/>.</param>
    /// <param name="fileToAttachPath">Путь к файлу, который нужно встроить.</param>
    /// <param name="description">Необязательное описание (<c>/Desc</c>); <see langword="null"/> — без
    /// описания.</param>
    /// <param name="ct">Отмена.</param>
    /// <exception cref="System.ArgumentException"><paramref name="sourcePath"/>,
    /// <paramref name="targetPath"/> либо <paramref name="fileToAttachPath"/> пуст / whitespace.</exception>
    Task AddAsync(
        string sourcePath, string targetPath, string fileToAttachPath, string? description, CancellationToken ct);

    /// <summary>
    /// Удаляет вложение с именем <paramref name="attachmentName"/> из копии PDF (запись из name-tree
    /// убирается). Если вложения с таким именем нет — no-op (валидный PDF, прочие вложения целы).
    /// </summary>
    /// <param name="sourcePath">Исходный PDF; не модифицируется.</param>
    /// <param name="targetPath">Куда записать результат. Может совпадать с <paramref name="sourcePath"/>.</param>
    /// <param name="attachmentName">Имя удаляемого вложения (<see cref="PdfAttachment.Name"/>).</param>
    /// <param name="ct">Отмена.</param>
    /// <exception cref="System.ArgumentException"><paramref name="sourcePath"/>,
    /// <paramref name="targetPath"/> либо <paramref name="attachmentName"/> пуст / whitespace.</exception>
    Task RemoveAsync(string sourcePath, string targetPath, string attachmentName, CancellationToken ct);
}
