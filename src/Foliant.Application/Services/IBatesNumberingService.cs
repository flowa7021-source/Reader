using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Наносит последовательную Bates-нумерацию (юридический штамп страниц) на PDF и пишет
/// результат в новый файл — оригинал не мутируется. Параллель к
/// <see cref="IHeaderFooterService"/>; шарит native gate с другими PDFium-сервисами.
///
/// <para>В отличие от header/footer текст не задаётся caller'ом: сервис сам формирует
/// <c>{Prefix}{номер:D{Digits}}{Suffix}</c> по <see cref="BatesNumberingSpec"/>. Счётчик
/// монотонен по всем страницам документа; <see cref="BatesNumberingSpec.Range"/> ограничивает
/// лишь набор штампуемых страниц, не сдвигая номера.</para>
///
/// Контракт ошибок:
/// <list type="bullet">
/// <item>Пустой/whitespace <c>sourcePath</c> или <c>targetPath</c> →
/// <see cref="System.ArgumentException"/>.</item>
/// <item><c>spec</c> = <c>null</c> → <see cref="System.ArgumentNullException"/>.</item>
/// <item>Неположительный <see cref="BatesNumberingSpec.FontSize"/> или
/// <see cref="BatesNumberingSpec.Digits"/> &lt; 1 →
/// <see cref="System.ArgumentOutOfRangeException"/>.</item>
/// <item>Битый PDF / IO-сбой → пробрасывается caller'у.</item>
/// </list>
/// </summary>
public interface IBatesNumberingService
{
    Task ApplyAsync(string sourcePath, BatesNumberingSpec spec, string targetPath, CancellationToken ct);
}
