using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Чтение и запись настроек начального вида PDF (<c>/PageLayout</c>, <c>/PageMode</c> +
/// <c>/ViewerPreferences</c>, «Initial View» в Acrobat, ISO 32000-1 §7.7.2 / §12.2): раскладка
/// страниц, режим панелей и UI-флаги, применяемые viewer'ом при открытии документа. Симметричный
/// read/write контракт, как у page-label / OCG-сервисов.
///
/// <para>Stateless; запись не мутирует оригинал — результат пишется в <c>targetPath</c> атомарно
/// (temp + Move) через инкрементальный апдейт (ISO 32000-1 §7.5.6), поэтому <c>sourcePath</c> может
/// совпадать с <c>targetPath</c>.</para>
/// </summary>
public interface IPdfViewerPreferencesService
{
    /// <summary>
    /// Читает настройки начального вида из <paramref name="pdfPath"/>. Документ без соответствующих
    /// ключей (или повреждённый) → <see cref="PdfViewerPreferences.Default"/> (best-effort, без
    /// throw'а). Отсутствующие флаги трактуются как <see langword="false"/>, неизвестные значения
    /// <c>/PageLayout</c> / <c>/PageMode</c> — как <see cref="PdfPageLayout.Default"/> /
    /// <see cref="PdfPageMode.Default"/>.
    /// </summary>
    /// <param name="pdfPath">Путь к PDF.</param>
    /// <param name="ct">Отмена.</param>
    /// <returns>Настройки начального вида (или <see cref="PdfViewerPreferences.Default"/>).</returns>
    /// <exception cref="System.ArgumentException"><paramref name="pdfPath"/> пуст / whitespace.</exception>
    Task<PdfViewerPreferences> ReadAsync(string pdfPath, CancellationToken ct);

    /// <summary>
    /// Записывает <paramref name="prefs"/> в копию PDF. <see cref="PdfPageLayout.Default"/> /
    /// <see cref="PdfPageMode.Default"/> опускают соответствующий ключ catalog'а; флаги, равные
    /// <see langword="false"/> (PDF-default), не пишутся, а если все пять флагов
    /// <see langword="false"/> — подсловарь <c>/ViewerPreferences</c> не создаётся вовсе.
    /// <see cref="PdfViewerPreferences.Default"/> → валидный PDF <b>без</b> этих ключей (существующие
    /// настройки начального вида отбрасываются).
    /// </summary>
    /// <param name="sourcePath">Исходный PDF; не модифицируется.</param>
    /// <param name="targetPath">Куда записать результат. Может совпадать с <paramref name="sourcePath"/>.</param>
    /// <param name="prefs">Настройки начального вида.</param>
    /// <param name="ct">Отмена.</param>
    /// <exception cref="System.ArgumentException"><paramref name="sourcePath"/> /
    /// <paramref name="targetPath"/> пуст / whitespace.</exception>
    /// <exception cref="System.ArgumentNullException"><paramref name="prefs"/> = <see langword="null"/>.</exception>
    Task WriteAsync(string sourcePath, string targetPath, PdfViewerPreferences prefs, CancellationToken ct);
}
