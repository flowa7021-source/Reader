using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Сканирование и удаление document-level скриптов / автоматических действий PDF (ISO 32000-1 §12.6 /
/// §12.7): catalog <c>/OpenAction</c>-JavaScript, document-level JavaScript name-tree
/// (<c>/Names → /JavaScript</c>) и document additional-actions (<c>/AA</c>). Полезно для «обеззараживания»
/// присланного PDF перед открытием. Симметричный scan/remove контракт, как у page-label, attachment и
/// OCG-сервисов.
///
/// <para>Stateless; удаление не мутирует оригинал — результат пишется в <c>targetPath</c> атомарно
/// (temp + Move) через инкрементальный апдейт (ISO 32000-1 §7.5.6), поэтому <c>sourcePath</c> может
/// совпадать с <c>targetPath</c>. Out of scope для этого MVP: per-page <c>/AA</c> и field-level
/// <c>/AA</c> (учитываются и удаляются только действия уровня catalog'а).</para>
/// </summary>
public interface IPdfSanitizationService
{
    /// <summary>
    /// Сканирует <paramref name="pdfPath"/> на наличие document-level скриптов / действий. Документ
    /// без них (или повреждённый / отсутствующий) → пустой отчёт (best-effort, без throw'а):
    /// <see cref="PdfSanitizationReport.HasAnyJavaScriptOrActions"/> = <see langword="false"/>.
    /// </summary>
    /// <param name="pdfPath">Путь к PDF.</param>
    /// <param name="ct">Отмена.</param>
    /// <returns>Отчёт о найденных скриптах / действиях (возможно пустой).</returns>
    /// <exception cref="System.ArgumentException"><paramref name="pdfPath"/> пуст / whitespace.</exception>
    Task<PdfSanitizationReport> ScanAsync(string pdfPath, CancellationToken ct);

    /// <summary>
    /// Удаляет из копии PDF document-level скрипты / действия: catalog <c>/OpenAction</c> —
    /// <b>только</b> если это JavaScript-действие (безопасный GoTo / destination-array open-action
    /// сохраняется), ветку <c>/Names → /JavaScript</c> (прочие подключи <c>/Names</c> вроде
    /// <c>/EmbeddedFiles</c> / <c>/Dests</c> сохраняются), а также catalog <c>/AA</c>.
    /// </summary>
    /// <param name="sourcePath">Исходный PDF; не модифицируется.</param>
    /// <param name="targetPath">Куда записать результат. Может совпадать с <paramref name="sourcePath"/>.</param>
    /// <param name="ct">Отмена.</param>
    /// <returns><see langword="true"/>, если хоть что-то было удалено; <see langword="false"/>, если
    /// удалять было нечего (в этом случае пишется валидная, но без изменений по содержимому
    /// перезаписанная копия).</returns>
    /// <exception cref="System.ArgumentException"><paramref name="sourcePath"/> либо
    /// <paramref name="targetPath"/> пуст / whitespace.</exception>
    Task<bool> RemoveJavaScriptAndActionsAsync(string sourcePath, string targetPath, CancellationToken ct);
}
