using System.Windows;
using Foliant.Application.Services;

namespace Foliant.UI;

/// <summary>
/// WPF-реализация <see cref="IPasswordPrompt"/>: показывает <see cref="PasswordPromptDialog"/>
/// модально на UI-потоке. Диалог синхронный (ShowDialog), поэтому метод оборачивает результат в
/// <see cref="Task.FromResult{TResult}(TResult)"/>. Маршалинг на UI-поток — через
/// <c>Application.Current.Dispatcher</c>: VM зовёт промпт из своего (возможно фонового) контекста.
///
/// <para>Public (не internal), т.к. регистрируется в DI из <c>Foliant.App</c>, у которого нет
/// InternalsVisibleTo к <c>Foliant.UI</c> — так же, как соседний <c>WpfPageImageExporter</c>.</para>
/// </summary>
public sealed class WpfPasswordPrompt : IPasswordPrompt
{
    public Task<string?> RequestPasswordAsync(string path, int attempt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(path);
        ct.ThrowIfCancellationRequested();

        // Полная квалификация: `using Foliant.Application.Services` вводит в скоуп namespace
        // `Foliant.Application`, из-за чего голый `Application` резолвился бы как namespace, а не
        // System.Windows.Application (CS0118). Берём WPF-Application явно.
        System.Windows.Application? app = System.Windows.Application.Current;
        if (app is null)
        {
            // Нет WPF-приложения (теоретический край: вызов вне UI-хоста) → нечего показывать.
            return Task.FromResult<string?>(null);
        }

        // Owner — активное главное окно, чтобы диалог центрировался и был модальным к нему.
        Window? owner = app.MainWindow;

        // Если уже на UI-потоке — показываем напрямую; иначе синхронно маршалим через Dispatcher.
        string? result = app.Dispatcher.CheckAccess()
            ? PasswordPromptDialog.Prompt(owner, path, attempt)
            : app.Dispatcher.Invoke(() => PasswordPromptDialog.Prompt(owner, path, attempt));

        return Task.FromResult<string?>(result);
    }
}
