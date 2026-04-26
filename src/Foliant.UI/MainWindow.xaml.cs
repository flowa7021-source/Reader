using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using Foliant.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Foliant.UI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ILogger<MainWindow> _logger;

    public MainWindow(MainViewModel vm, ILogger<MainWindow> logger)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(logger);

        _vm = vm;
        _logger = logger;

        InitializeComponent();
        DataContext = _vm;

        ThemeManager.Apply(_vm.CurrentTheme, Application.Current);

        _vm.PropertyChanged += OnViewModelPropertyChanged;
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
            await _vm.InitializeAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize main view model.");
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI event handler must not propagate exceptions.")]
    private async void OnOpenMenuItemClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open Document",
            Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        string path = dialog.FileName;
        using var cts = new CancellationTokenSource();

        try
        {
            await _vm.OpenDocumentFromPathAsync(path, cts.Token).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error opening document '{Path}'.", path);
            MessageBox.Show(this, ex.Message, "Error opening document", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnExitMenuItemClick(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }
}
