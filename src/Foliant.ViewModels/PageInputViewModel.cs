using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Foliant.ViewModels;

/// <summary>
/// VM модального диалога «Go to page N». Принимает текстовый ввод, валидирует
/// (целое число в диапазоне [1..PageCount]), при успешной валидации вызывает
/// <c>onAccept(zeroBasedIndex)</c> — caller сам решает, что делать с результатом
/// (обычно — <c>tab.GoToPageCommand.Execute</c>).
/// </summary>
public sealed partial class PageInputViewModel : ObservableObject
{
    private readonly Action<int> _onAccept;
    private readonly Action _onCancel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    [NotifyPropertyChangedFor(nameof(ErrorMessage))]
    [NotifyCanExecuteChangedFor(nameof(AcceptCommand))]
    private string _input = string.Empty;

    public int PageCount { get; }

    public PageInputViewModel(int pageCount, Action<int> onAccept, Action? onCancel = null)
    {
        ArgumentNullException.ThrowIfNull(onAccept);
        ArgumentOutOfRangeException.ThrowIfNegative(pageCount);
        PageCount = pageCount;
        _onAccept = onAccept;
        _onCancel = onCancel ?? (() => { });
    }

    /// <summary>True если <see cref="Input"/> парсится в число и попадает в [1..PageCount].</summary>
    public bool IsValid => TryParse(Input, out _);

    /// <summary>Текст ошибки для UI (или <c>null</c> когда ввод валиден или пуст).</summary>
    public string? ErrorMessage
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Input))
            {
                return null;
            }

            return TryParse(Input, out _) ? null : $"Enter a page number from 1 to {PageCount}.";
        }
    }

    /// <summary>Команда «OK» / Enter в TextBox.</summary>
    [RelayCommand(CanExecute = nameof(IsValid))]
    private void Accept()
    {
        if (!TryParse(Input, out int oneBased))
        {
            return;
        }
        _onAccept(oneBased - 1);   // caller ожидает 0-based
    }

    [RelayCommand]
    private void Cancel() => _onCancel();

    private bool TryParse(string raw, out int oneBased)
    {
        oneBased = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out oneBased))
        {
            return false;
        }
        return oneBased >= 1 && oneBased <= PageCount;
    }
}
