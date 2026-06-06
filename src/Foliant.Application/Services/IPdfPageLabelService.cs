using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Чтение и запись page-label'ов PDF (<c>/PageLabels</c>, «Number Pages» в Acrobat, ISO 32000-1
/// §12.4.2): диапазоны с собственным стилем нумерации (i, ii, iii → 1, 2, 3 → A-1, A-2 …), которые
/// показываются в навигаторе вместо физического индекса страницы. Симметричный read/write контракт,
/// как у OCG-сервиса.
///
/// <para>Stateless; запись не мутирует оригинал — результат пишется в <c>targetPath</c> атомарно
/// (temp + Move) через инкрементальный апдейт (ISO 32000-1 §7.5.6), поэтому <c>sourcePath</c> может
/// совпадать с <c>targetPath</c>.</para>
/// </summary>
public interface IPdfPageLabelService
{
    /// <summary>
    /// Читает диапазоны page-label из <paramref name="pdfPath"/>. Документ без <c>/PageLabels</c>
    /// (или повреждённый) → пустой список (best-effort, без throw'а). Результат отсортирован по
    /// <see cref="PdfPageLabelRange.StartPageIndex"/>.
    /// </summary>
    /// <param name="pdfPath">Путь к PDF.</param>
    /// <param name="ct">Отмена.</param>
    /// <returns>Диапазоны нумерации (возможно пустой список).</returns>
    /// <exception cref="System.ArgumentException"><paramref name="pdfPath"/> пуст / whitespace.</exception>
    Task<IReadOnlyList<PdfPageLabelRange>> ReadAsync(string pdfPath, CancellationToken ct);

    /// <summary>
    /// Записывает <paramref name="ranges"/> в <c>/PageLabels</c> копии PDF. Пустой список →
    /// валидный PDF <b>без</b> <c>/PageLabels</c> (существующая нумерация отбрасывается). Диапазоны
    /// сортируются по <see cref="PdfPageLabelRange.StartPageIndex"/>; дубликат стартового индекса —
    /// ошибка (number-tree ключи уникальны).
    /// </summary>
    /// <param name="sourcePath">Исходный PDF; не модифицируется.</param>
    /// <param name="targetPath">Куда записать результат. Может совпадать с <paramref name="sourcePath"/>.</param>
    /// <param name="ranges">Диапазоны нумерации.</param>
    /// <param name="ct">Отмена.</param>
    /// <exception cref="System.ArgumentException"><paramref name="sourcePath"/> /
    /// <paramref name="targetPath"/> пуст, либо в <paramref name="ranges"/> есть дубликат стартового
    /// индекса / отрицательный индекс.</exception>
    /// <exception cref="System.ArgumentNullException"><paramref name="ranges"/> = <see langword="null"/>.</exception>
    Task WriteAsync(string sourcePath, string targetPath, IReadOnlyList<PdfPageLabelRange> ranges, CancellationToken ct);
}
