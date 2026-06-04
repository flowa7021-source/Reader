using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using Foliant.UI.Localization;

namespace Foliant.UI;

/// <summary>
/// Модальный диалог запроса пароля для открытия зашифрованного PDF. Маскированный
/// <c>PasswordBox</c> + Open/Cancel + подсказка (имя файла; при повторе — «wrong password»).
/// Возвращает введённый пароль из <see cref="Prompt"/> либо <c>null</c> при отмене.
///
/// <para>Зеркалит self-<c>DataContext</c> + статик-<c>Prompt</c> форму <see cref="InputDialog"/>.
/// Имена свойств (<c>PromptMessage</c>, <c>EnteredPassword</c>, <c>IsRetry</c>) сознательно НЕ
/// совпадают с зарезервированными членами <see cref="Window"/> — иначе CS0108 shadowing (урок
/// W2/W3 о cross-platform blind-spot'ах в этом WPF-only проекте). <c>PasswordBox.Password</c> не
/// bindable by design, поэтому читается в code-behind OK-handler'е — диалог и так code-behind.</para>
/// </summary>
public partial class PasswordPromptDialog : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _promptMessage = string.Empty;
    private bool _isRetry;

    public PasswordPromptDialog()
    {
        InitializeComponent();
        DataContext = this;
        // Фокус сразу в поле пароля, чтобы пользователь мог печатать без лишнего клика.
        Loaded += (_, _) => PasswordInput.Focus();
    }

    /// <summary>Подсказка над полем (имя файла + базовый текст). Ставится из <see cref="Prompt"/>.</summary>
    public string PromptMessage
    {
        get => _promptMessage;
        set
        {
            _promptMessage = value;
            Notify();
        }
    }

    /// <summary>True после неверного пароля — показывает «wrong password»-баннер.</summary>
    public bool IsRetry
    {
        get => _isRetry;
        set
        {
            _isRetry = value;
            Notify();
            Notify(nameof(RetryVisibility));
        }
    }

    /// <summary>Видимость retry-баннера, производная от <see cref="IsRetry"/>.</summary>
    public Visibility RetryVisibility => IsRetry ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Введённый пароль (выставляется из OK-handler'а). <c>null</c> до подтверждения.</summary>
    public string? EnteredPassword { get; private set; }

    /// <summary>
    /// Показать диалог модально. <paramref name="path"/> — путь к файлу (для подсказки),
    /// <paramref name="attempt"/> == 0 для первого запроса, &gt; 0 после неверного пароля.
    /// Возвращает введённый пароль или <c>null</c>, если пользователь отменил.
    /// </summary>
    public static string? Prompt(Window? owner, string path, int attempt)
    {
        ArgumentNullException.ThrowIfNull(path);

        var loc = LocalizationManager.Instance;
        string fileName = Path.GetFileName(path);
        var dialog = new PasswordPromptDialog
        {
            Owner = owner,
            // Имя файла + базовая подсказка: «<file>\n<hint>».
            PromptMessage = $"{fileName}\n{loc["PasswordPromptMessage"]}",
            IsRetry = attempt > 0,
        };

        return dialog.ShowDialog() == true ? dialog.EnteredPassword : null;
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        // PasswordBox.Password читаем здесь (не bindable). Пустой пароль допустим — PDFium сам
        // решит, подходит ли он; пустая строка отличается от null (отмена).
        EnteredPassword = PasswordInput.Password;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
