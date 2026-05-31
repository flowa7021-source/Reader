using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using Foliant.UI.Localization;
using Foliant.ViewModels;
using Microsoft.Win32;

namespace Foliant.UI;

/// <summary>
/// Интерактивное заполнение AcroForm-полей PDF (Q-F24 UI). Загружает поля через
/// <see cref="DocumentTabViewModel.ReadFormFieldsAsync"/> при открытии, позволяет править
/// значения в <c>DataGrid</c>, и по «Save As…» пишет заполненный PDF через
/// <see cref="DocumentTabViewModel.FillFormCommand"/>.
/// </summary>
public partial class FormFillDialog : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly DocumentTabViewModel _tab;

    public FormFillDialog(DocumentTabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        _tab = tab;
        InitializeComponent();
        DataContext = this;
        Loaded += OnLoadedAsync;
    }

    public ObservableCollection<FormFieldEntry> Fields { get; } = [];

    public bool HasFields => Fields.Count > 0;

    /// <summary>Заголовок: «N полей» либо подсказка про отсутствие полей формы.</summary>
    public string HeaderText
    {
        get
        {
            var loc = LocalizationManager.Instance;
            if (Fields.Count == 0)
            {
                return loc["FormFillEmptyHint"];
            }

            return loc["FormFillCountFormat"].Replace(
                "{0}",
                Fields.Count.ToString(CultureInfo.CurrentCulture),
                StringComparison.Ordinal);
        }
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        // ReadFormFieldsAsync — fail-soft: внутри логирует и возвращает пустой список при
        // ошибке чтения, поэтому здесь try/catch не нужен (пустой список → подсказка во HeaderText).
        var fields = await _tab.ReadFormFieldsAsync(CancellationToken.None).ConfigureAwait(true);
        Fields.Clear();
        foreach (var f in fields)
        {
            Fields.Add(f);
        }

        Notify(nameof(HasFields));
        Notify(nameof(HeaderText));
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI event handler must not propagate exceptions.")]
    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (Fields.Count == 0)
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        var save = new SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            Title = loc["FormFillSaveTitle"],
            FileName = Path.GetFileNameWithoutExtension(_tab.FilePath) + "-filled.pdf",
        };
        if (save.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var request = new FillFormRequest([.. Fields], save.FileName);
            await _tab.FillFormCommand.ExecuteAsync(request);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, loc["ErrorDialogTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
