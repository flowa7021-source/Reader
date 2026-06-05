namespace Foliant.Application.Services;

/// <summary>
/// Запрашивает у пользователя пароль для открытия зашифрованного документа. Реализуется
/// в UI-слое (модальный диалог). Опционален: в headless/тестовом окружении не регистрируется,
/// и VM тогда пробрасывает <c>DocumentPasswordRequiredException</c> в обычный обработчик ошибок.
/// </summary>
public interface IPasswordPrompt
{
    /// <summary>
    /// Показать запрос пароля. <paramref name="attempt"/> == 0 для первого запроса; &gt; 0
    /// после неверного пароля (View покажет «wrong password»). Возвращает введённый пароль,
    /// либо <c>null</c>, если пользователь отменил.
    /// </summary>
    Task<string?> RequestPasswordAsync(string path, int attempt, CancellationToken ct);
}
