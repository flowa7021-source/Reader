using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using Foliant.UI.Localization;
using Foliant.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Foliant.UI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly Func<SettingsWindow> _settingsWindowFactory;
    private readonly ILogger<MainWindow> _logger;

    public MainWindow(MainViewModel vm, Func<SettingsWindow> settingsWindowFactory, ILogger<MainWindow> logger)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(settingsWindowFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _vm = vm;
        _settingsWindowFactory = settingsWindowFactory;
        _logger = logger;

        InitializeComponent();
        DataContext = _vm;

        ThemeManager.Apply(_vm.CurrentTheme, Application.Current);

        _vm.PropertyChanged += OnViewModelPropertyChanged;
        Closed += OnWindowClosed;
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _vm.PropertyChanged -= OnViewModelPropertyChanged;
        Closed -= OnWindowClosed;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentTheme))
        {
            ThemeManager.Apply(_vm.CurrentTheme, Application.Current);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI event handler must not propagate exceptions.")]
    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _vm.InitializeAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize main view model.");
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI event handler must not propagate exceptions.")]
    private async void OnOpenMenuItemClick(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        var dialog = new OpenFileDialog
        {
            Title = loc["OpenDocumentDialogTitle"],
            Filter = loc["OpenDocumentDialogFilter"],
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        string path = dialog.FileName;

        try
        {
            await _vm.OpenDocumentFromPathAsync(path, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error opening document '{Path}'.", path);
            MessageBox.Show(this, ex.Message, loc["ErrorDialogTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnSettingsMenuItemClick(object sender, RoutedEventArgs e)
    {
        var settingsWin = _settingsWindowFactory();
        settingsWin.Owner = this;

        if (settingsWin.ShowDialog() == true)
        {
            // Theme may have changed — apply immediately.
            _vm.CurrentTheme = settingsWin.ViewModel.SelectedTheme;
        }
    }

    private void OnExitMenuItemClick(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }
}
