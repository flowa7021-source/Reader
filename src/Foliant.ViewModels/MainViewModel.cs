using CommunityToolkit.Mvvm.ComponentModel;

namespace Foliant.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "Foliant";

    [ObservableProperty]
    private string _statusMessage = string.Empty;
}
