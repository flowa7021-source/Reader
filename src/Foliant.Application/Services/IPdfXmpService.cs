namespace Foliant.Application.Services;

/// <summary>
/// Чтение / запись document-level XMP-метаданных PDF (XMP metadata packet, ISO 32000-1 §14.3.2):
/// catalog-ключ <c>/Metadata</c> ссылается на metadata-поток <c>&lt;&lt; /Type /Metadata /Subtype /XML
/// /Length N &gt;&gt;</c>, тело которого — сырой XMP-пакет (UTF-8 XML), по соглашению несжатый (чтобы
/// читался без разбора PDF). Симметричный read/write контракт, как у attachment / page-label /
/// viewer-preferences-сервисов.
///
/// <para>Stateless; запись не мутирует оригинал — результат пишется в <c>targetPath</c> атомарно
/// (temp + Move) через инкрементальный апдейт (ISO 32000-1 §7.5.6), поэтому <c>sourcePath</c> может
/// совпадать с <c>targetPath</c>.</para>
/// </summary>
public interface IPdfXmpService
{
    /// <summary>
    /// Читает XMP-пакет документа из <paramref name="pdfPath"/> как UTF-8-строку. Документ без
    /// <c>/Metadata</c> → <see langword="null"/>. Чтение best-effort: повреждённый PDF / нечитаемый
    /// поток → <see langword="null"/> (без throw'а).
    /// </summary>
    /// <param name="pdfPath">Путь к PDF.</param>
    /// <param name="ct">Отмена.</param>
    /// <returns>XMP-пакет как UTF-8-строка либо <see langword="null"/>, если <c>/Metadata</c> нет.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="pdfPath"/> пуст / whitespace.</exception>
    Task<string?> ReadAsync(string pdfPath, CancellationToken ct);

    /// <summary>
    /// Заменяет (или создаёт) catalog-поток <c>/Metadata</c> на <paramref name="xmpPacket"/>: его
    /// UTF-8-байты выписываются в metadata-поток <c>&lt;&lt; /Type /Metadata /Subtype /XML
    /// /Length N &gt;&gt;</c> без сжатия. Запись атомарна (temp + Move), <paramref name="sourcePath"/>
    /// не модифицируется, <paramref name="sourcePath"/> может совпадать с <paramref name="targetPath"/>.
    /// Пустой <paramref name="xmpPacket"/> допустим (пишется пустой пакет).
    /// </summary>
    /// <param name="sourcePath">Исходный PDF; не модифицируется.</param>
    /// <param name="targetPath">Куда записать результат. Может совпадать с <paramref name="sourcePath"/>.</param>
    /// <param name="xmpPacket">XMP-пакет (UTF-8 XML); пустая строка допустима, <see langword="null"/> — нет.</param>
    /// <param name="ct">Отмена.</param>
    /// <exception cref="System.ArgumentException"><paramref name="sourcePath"/> либо
    /// <paramref name="targetPath"/> пуст / whitespace.</exception>
    /// <exception cref="System.ArgumentNullException"><paramref name="xmpPacket"/> равен
    /// <see langword="null"/>.</exception>
    Task WriteAsync(string sourcePath, string targetPath, string xmpPacket, CancellationToken ct);
}
