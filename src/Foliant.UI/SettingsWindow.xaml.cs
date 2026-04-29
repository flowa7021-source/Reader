using System.Diagnostics.CodeAnalysis;
using System.Windows;
using Foliant.ViewModels;

namespace Foliant.UI;

public partial class SettingsWindow : Window
{
    public SettingsViewModel ViewModel { get; }

    public SettingsWindow(SettingsViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);

        ViewModel = vm;
        InitializeComponent();
        DataContext = ViewModel;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI event handler must not propagate exceptions.")]
    private async void OnOkClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.SaveCommand.ExecuteAsync(null);
            DialogResult = true;
        }
        catch (Exception)
        {
            DialogResult = false;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
