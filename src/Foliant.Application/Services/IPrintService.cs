using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Печатает страницы документа через системный диалог принтера (UI-уровень). Реализация
/// (WPF в боевом приложении) сама показывает <c>PrintDialog</c>, считывает выбор пользователя
/// (принтер + диапазон страниц), рендерит выбранные страницы через
/// <see cref="IDocument.RenderPageAsync(int, RenderOptions, CancellationToken)"/> и отправляет
/// в спулер. VM делегирует команду без знания о WPF — так печать переживёт смену UI-фреймворка
/// и остаётся document-neutral (заработает для любого <see cref="IDocument"/>, не только PDF).
/// Опционален в DI: в headless/тестовом окружении не регистрируется, и тогда VM-команда
/// просто отключается (<c>CanPrint = false</c>) — это безопасный default.
/// </summary>
public interface IPrintService
{
    /// <summary>
    /// Показать системный <c>PrintDialog</c> для <paramref name="document"/> и распечатать
    /// выбранный диапазон. <paramref name="documentTitle"/> попадает в имя job'а у спулера
    /// (то, что видно в очереди печати). Возвращает <c>true</c>, если пользователь подтвердил
    /// печать, и <c>false</c>, если отменил диалог.
    /// </summary>
    Task<bool> PrintAsync(IDocument document, string documentTitle, CancellationToken ct);
}
