using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Управление именованными пунктами назначения PDF (named destinations, ISO 32000-1 §12.3.2): список /
/// добавление / удаление записей «имя → целевая страница», хранящихся в catalog name-tree
/// <c>/Names → /Dests</c> (modern) и/или legacy-словаре catalog <c>/Dests</c>. Симметричный
/// read/write контракт, как у attachment- и page-label-сервисов.
///
/// <para>Stateless; запись не мутирует оригинал — результат пишется в <c>targetPath</c> атомарно
/// (temp + Move) через инкрементальный апдейт (ISO 32000-1 §7.5.6), поэтому <c>sourcePath</c> может
/// совпадать с <c>targetPath</c>. Чтение учитывает обе формы хранения; запись идёт только в
/// modern-форму <c>/Names → /Dests</c>.</para>
/// </summary>
public interface IPdfNamedDestinationService
{
    /// <summary>
    /// Читает список именованных пунктов назначения из <paramref name="pdfPath"/>, учитывая обе формы
    /// хранения (modern <c>/Names → /Dests</c> и legacy <c>/Dests</c>). Документ без пунктов назначения
    /// (или повреждённый) → пустой список (best-effort, без throw'а). Записи, чью целевую страницу не
    /// удалось разрешить, пропускаются. Результат отсортирован по <see cref="PdfNamedDestination.Name"/>
    /// (ordinal).
    /// </summary>
    /// <param name="pdfPath">Путь к PDF.</param>
    /// <param name="ct">Отмена.</param>
    /// <returns>Именованные пункты назначения документа (возможно пустой список).</returns>
    /// <exception cref="System.ArgumentException"><paramref name="pdfPath"/> пуст / whitespace.</exception>
    Task<IReadOnlyList<PdfNamedDestination>> ListAsync(string pdfPath, CancellationToken ct);

    /// <summary>
    /// Добавляет (или заменяет) именованный пункт назначения <paramref name="name"/>, указывающий на
    /// страницу <paramref name="pageIndex"/>, в copy PDF: в modern name-tree <c>/Names → /Dests</c>
    /// выписывается запись <c>(name) [pageRef /Fit]</c>. Если пункт с таким именем уже есть в modern-форме
    /// — он заменяется. <paramref name="pageIndex"/> зажимается в <c>[0, pageCount-1]</c>. Существующие
    /// пункты с другими именами сохраняются.
    /// </summary>
    /// <param name="sourcePath">Исходный PDF; не модифицируется.</param>
    /// <param name="targetPath">Куда записать результат. Может совпадать с <paramref name="sourcePath"/>.</param>
    /// <param name="name">Имя пункта назначения (<see cref="PdfNamedDestination.Name"/>).</param>
    /// <param name="pageIndex">0-based индекс целевой страницы; зажимается в <c>[0, pageCount-1]</c>.</param>
    /// <param name="ct">Отмена.</param>
    /// <exception cref="System.ArgumentException"><paramref name="sourcePath"/>,
    /// <paramref name="targetPath"/> либо <paramref name="name"/> пуст / whitespace.</exception>
    Task AddAsync(string sourcePath, string targetPath, string name, int pageIndex, CancellationToken ct);

    /// <summary>
    /// Удаляет именованный пункт назначения <paramref name="name"/> из copy PDF (запись из modern
    /// name-tree <c>/Names → /Dests</c> убирается). Если пункта с таким именем в modern-форме нет — no-op
    /// (валидный PDF, прочие пункты целы). MVP-ограничение: пункт, существующий только в legacy-словаре
    /// catalog <c>/Dests</c>, этим методом не удаляется (legacy-форма при записи не трогается).
    /// </summary>
    /// <param name="sourcePath">Исходный PDF; не модифицируется.</param>
    /// <param name="targetPath">Куда записать результат. Может совпадать с <paramref name="sourcePath"/>.</param>
    /// <param name="name">Имя удаляемого пункта назначения (<see cref="PdfNamedDestination.Name"/>).</param>
    /// <param name="ct">Отмена.</param>
    /// <exception cref="System.ArgumentException"><paramref name="sourcePath"/>,
    /// <paramref name="targetPath"/> либо <paramref name="name"/> пуст / whitespace.</exception>
    Task RemoveAsync(string sourcePath, string targetPath, string name, CancellationToken ct);
}
