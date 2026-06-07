using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Чтение и запись <b>пользовательских</b> (custom) свойств документа PDF — нестандартных пар
/// ключ/значение в <c>/Info</c> dictionary (Acrobat → Document Properties → вкладка «Custom»,
/// ISO 32000-1 §14.3.3). Дополняет <see cref="IPdfMetadataEditService"/>, который ведает только 9
/// стандартными полями (<c>/Title /Author /Subject /Keywords /Creator /Producer /CreationDate /ModDate
/// /Trapped</c>): этот сервис трогает <b>исключительно</b> остальные ключи и стандартные поля при
/// записи сохраняет нетронутыми. Симметричный read/write контракт, как у page-label и attachment-сервисов.
///
/// <para>Stateless; запись не мутирует оригинал — результат пишется в <c>targetPath</c> атомарно
/// (temp + Move) через инкрементальный апдейт (ISO 32000-1 §7.5.6), поэтому <c>sourcePath</c> может
/// совпадать с <c>targetPath</c>.</para>
/// </summary>
public interface IPdfCustomPropertiesService
{
    /// <summary>
    /// Читает пользовательские (нестандартные) свойства из <c>/Info</c> документа
    /// <paramref name="pdfPath"/>. Документ без <c>/Info</c> либо только со стандартными полями (а
    /// также повреждённый) → пустой список (best-effort, без throw'а). Результат отсортирован по
    /// <see cref="PdfCustomProperty.Name"/> (ordinal).
    /// </summary>
    /// <param name="pdfPath">Путь к PDF.</param>
    /// <param name="ct">Отмена.</param>
    /// <returns>Пользовательские свойства документа (возможно пустой список).</returns>
    /// <exception cref="System.ArgumentException"><paramref name="pdfPath"/> пуст / whitespace.</exception>
    Task<IReadOnlyList<PdfCustomProperty>> ListAsync(string pdfPath, CancellationToken ct);

    /// <summary>
    /// Заменяет в копии PDF <b>весь</b> набор пользовательских свойств на
    /// <paramref name="customProperties"/>: прежние нестандартные ключи <c>/Info</c> удаляются,
    /// записываются только переданные. Стандартные поля (<c>/Title /Author …</c>) сохраняются
    /// нетронутыми. Пустой список → все пользовательские ключи удаляются (стандартные остаются).
    /// </summary>
    /// <param name="sourcePath">Исходный PDF; не модифицируется.</param>
    /// <param name="targetPath">Куда записать результат. Может совпадать с <paramref name="sourcePath"/>.</param>
    /// <param name="customProperties">Полный новый набор пользовательских свойств. Имена уникальны
    /// (ordinal) и непусты; значение может быть любой строкой, включая пустую.</param>
    /// <param name="ct">Отмена.</param>
    /// <exception cref="System.ArgumentException"><paramref name="sourcePath"/> /
    /// <paramref name="targetPath"/> пуст / whitespace, либо в <paramref name="customProperties"/> есть
    /// дубликат имени (ordinal) или свойство с пустым / whitespace именем.</exception>
    /// <exception cref="System.ArgumentNullException"><paramref name="customProperties"/> =
    /// <see langword="null"/>.</exception>
    /// <exception cref="System.NotSupportedException">В исходном документе нет <c>/Info</c> dictionary:
    /// инкрементальный апдейт не может добавить <c>/Info</c> в trailer, поэтому записать
    /// custom-свойства в такой документ нельзя (сначала задайте любое стандартное поле через
    /// <see cref="IPdfMetadataEditService"/>).</exception>
    Task SetAsync(
        string sourcePath,
        string targetPath,
        IReadOnlyList<PdfCustomProperty> customProperties,
        CancellationToken ct);
}
