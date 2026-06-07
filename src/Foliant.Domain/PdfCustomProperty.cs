namespace Foliant.Domain;

/// <summary>
/// Одно пользовательское (custom) свойство документа PDF — пара ключ/значение в <c>/Info</c>
/// dictionary с ключом, <b>не</b> входящим в набор стандартных полей (Acrobat → Document Properties →
/// вкладка «Custom»; ISO 32000-1 §14.3.3). Стандартные ключи (<c>/Title /Author /Subject /Keywords
/// /Creator /Producer /CreationDate /ModDate /Trapped</c>) обрабатываются <c>IPdfMetadataEditService</c>
/// и сюда не относятся; всё остальное в <c>/Info</c> — custom-свойство (например <c>/Company</c>,
/// <c>/Status</c>).
///
/// <para>Pure-data, immutable. <c>with</c>-копии идут мимо какой-либо валидации — это допустимо, как и
/// у других domain-record'ов; проверка имени/значения — на стороне сервиса записи.</para>
/// </summary>
/// <param name="Name">Имя свойства = ключ <c>/Info</c> (PDF name без ведущего <c>/</c>), например
/// <c>Company</c>. Культур-независимое, чувствительно к регистру.</param>
/// <param name="Value">Значение свойства (PDF text-string). Произвольная строка, в т.ч. Unicode и
/// пустая; без транслитерации.</param>
public sealed record PdfCustomProperty(string Name, string Value);
