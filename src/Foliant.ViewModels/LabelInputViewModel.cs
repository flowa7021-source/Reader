using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Foliant.ViewModels;

/// <summary>
/// Универсальный модальный VM для ввода однострочного label — используется
/// для rename bookmark / annotation note / file alias и т.п. Caller подаёт
/// <c>initialText</c> и callback'и; VM возвращает trimmed-текст в
/// <c>onAccept</c> при валидном вводе. Пустая строка считается невалидной.
/// </summary>
public sealed partial class LabelInputViewModel : ObservableObject
{
    private readonly Action<string> _onAccept;
    private readonly Action _onCancel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    [NotifyCanExecuteChangedFor(nameof(AcceptCommand))]
    private string _input;

    /// <summary>Заголовок диалога (например, "Rename bookmark"). Локализуется на UI-стороне.</summary>
    public string DialogTitle { get; }

    /// <summary>Подсказка над input-ом (placeholder / label).</summary>
    public string Prompt { get; }

    /// <summary>Лимит длины (для <c>MaxLength</c> на TextBox). 0 = без ограничения.</summary>
    public int MaxLength { get; }

    public bool IsValid => !string.IsNullOrWhiteSpace(Input)
                           && (MaxLength == 0 || Input.Length <= MaxLength);

    public LabelInputViewModel(
        string dialogTitle,
        string prompt,
        string initialText,
        Action<string> onAccept,
        Action? onCancel = null,
        int maxLength = 0)
    {
        ArgumentNullException.ThrowIfNull(dialogTitle);
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(initialText);
        ArgumentNullException.ThrowIfNull(onAccept);
        ArgumentOutOfRangeException.ThrowIfNegative(maxLength);

        DialogTitle = dialogTitle;
        Prompt = prompt;
        _input = initialText;
        _onAccept = onAccept;
        _onCancel = onCancel ?? (() => { });
        MaxLength = maxLength;
    }

    [RelayCommand(CanExecute = nameof(IsValid))]
    private void Accept() => _onAccept(Input.Trim());

    [RelayCommand]
    private void Cancel() => _onCancel();
}
