using System.Windows;
using Foliant.ViewModels;

namespace Foliant.UI;

public partial class CrashRecoveryWindow : Window
{
    public CrashRecoveryViewModel ViewModel { get; }

    public CrashRecoveryWindow(CrashRecoveryViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);

        ViewModel = vm;
        InitializeComponent();
        DataContext = ViewModel;
    }
}
