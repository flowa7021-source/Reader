using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Накладывает текстовые колонтитулы на каждую страницу PDF и пишет результат в новый файл.
/// Параллель к <see cref="IWatermarkService"/>; шарит native gate с другими PDFium-сервисами.
///
/// Placeholder'ы (<c>{page}</c>/<c>{total}</c>/<c>{filename}</c>/<c>{date}</c>) расширяются
/// per-page внутри реализации — caller передаёт литералы и шаблоны вперемешку.
///
/// Контракт ошибок:
/// <list type="bullet">
/// <item><see cref="HeaderFooterSpec.Bands"/> пуст или у всех bands <c>Text</c> blank —
/// <see cref="ArgumentException"/> (нечего рисовать).</item>
/// <item>Отрицательный font size → <see cref="ArgumentOutOfRangeException"/>.</item>
/// <item>Битый PDF / IO-сбой → пробрасывается caller'у.</item>
/// </list>
/// </summary>
public interface IHeaderFooterService
{
    Task ApplyAsync(string sourcePath, HeaderFooterSpec spec, string targetPath, CancellationToken ct);
}
