using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace Foliant.UI;

/// <summary>
/// Минимальный input-dialog: заголовок, метка, single-line TextBox, OK/Cancel. Возвращает
/// <c>null</c> при отмене (Cancel / Esc / закрытие крестиком), иначе trimmed-значение TextBox'а.
/// Пустой/whitespace-результат тоже допустим — caller сам решает, как трактовать.
///
/// Используется bookmark-rename'ом и annotation-subject-editor'ом — оба обращаются через
/// <see cref="Prompt"/>; собственного MVVM-VM не имеет, чтобы не плодить артефакты ради одного
/// поля.
/// </summary>
public partial class InputDialog : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _dialogTitle = string.Empty;
    private string _label = string.Empty;
    private string _value = string.Empty;

    public InputDialog()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) =>
        {
            // На открытии — фокус и выделение всего текста, чтобы Esc/Enter работали без лишних кликов
            // и пользователь сразу мог начать печатать поверх старого значения.
            InputTextBox.Focus();
            InputTextBox.SelectAll();
        };
    }

    public string DialogTitle
    {
        get => _dialogTitle;
        set { _dialogTitle = value; Notify(); }
    }

    public string Label
    {
        get => _label;
        set { _label = value; Notify(); }
    }

    public string InputText
    {
        get => _value;
        set { _value = value; Notify(); }
    }

    /// <summary>Открыть диалог и вернуть результат. <c>null</c> — пользователь отменил.</summary>
    public static string? Prompt(Window? owner, string title, string label, string initialValue)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(initialValue);

        var dialog = new InputDialog
        {
            Owner = owner,
            DialogTitle = title,
            Label = label,
            InputText = initialValue,
        };

        return dialog.ShowDialog() == true ? dialog.InputText : null;
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        // Enter обрабатывается IsDefault на OK; Esc — IsCancel. Здесь только хук на будущее
        // (multi-line поддержка / валидация).
        _ = e;
    }

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
