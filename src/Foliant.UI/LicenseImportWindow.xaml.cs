using System.Windows;
using Foliant.ViewModels;

namespace Foliant.UI;

public partial class LicenseImportWindow : Window
{
    public LicenseImportViewModel ViewModel { get; }

    public LicenseImportWindow(LicenseImportViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);

        ViewModel = vm;
        InitializeComponent();
        DataContext = ViewModel;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
