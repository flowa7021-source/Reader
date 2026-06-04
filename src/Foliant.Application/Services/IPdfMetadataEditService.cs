using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Правит classic PDF <c>/Info</c>-метаданные (Title/Author/Subject/Keywords/Creator/Producer)
/// и пишет результат в новый файл. Оригинал не мутируется (atomic temp + Move, как
/// <c>IPdfSplitService</c> / <c>IPageRangeExtractor</c>). Реализация stateless.
///
/// Слияние полей по <see cref="PdfMetadataSpec"/>: <c>null</c>-поле сохраняет текущее
/// значение source, пустая строка очищает, непустая — перезаписывает (см. doc на record).
///
/// Fidelity: реализация ре-сериализует документ целиком. Страницы, контент и аннотации
/// (Text/Link и др.) сохраняются; XMP metadata stream и история incremental-update —
/// нет (заменяются на свежий <c>/Info</c>). Это приемлемо для metadata-edit.
///
/// Контракт ошибок:
/// <list type="bullet">
/// <item>Blank <c>sourcePath</c> или <c>targetPath</c> →
/// <see cref="ArgumentException"/>.</item>
/// <item><c>null</c> <c>spec</c> → <see cref="ArgumentNullException"/>.</item>
/// <item>Битый PDF / IO-сбой → пробрасывается caller'у
/// (<see cref="System.IO.IOException"/> или <see cref="InvalidOperationException"/>).</item>
/// </list>
/// </summary>
public interface IPdfMetadataEditService
{
    /// <summary>
    /// Читает <paramref name="sourcePath"/>, сливает его <c>/Info</c> с <paramref name="spec"/>
    /// и пишет новый PDF (все страницы + новый <c>/Info</c>) в <paramref name="targetPath"/>.
    /// </summary>
    Task EditAsync(string sourcePath, string targetPath, PdfMetadataSpec spec, CancellationToken ct);
}
