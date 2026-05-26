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
    private readonly Func<CrashRecoveryWindow> _crashRecoveryWindowFactory;
    private readonly Func<LicenseImportWindow> _licenseWindowFactory;
    private readonly ILogger<MainWindow> _logger;

    public MainWindow(
        MainViewModel vm,
        Func<SettingsWindow> settingsWindowFactory,
        Func<CrashRecoveryWindow> crashRecoveryWindowFactory,
        Func<LicenseImportWindow> licenseWindowFactory,
        ILogger<MainWindow> logger)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(settingsWindowFactory);
        ArgumentNullException.ThrowIfNull(crashRecoveryWindowFactory);
        ArgumentNullException.ThrowIfNull(licenseWindowFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _vm = vm;
        _settingsWindowFactory = settingsWindowFactory;
        _crashRecoveryWindowFactory = crashRecoveryWindowFactory;
        _licenseWindowFactory = licenseWindowFactory;
        _logger = logger;

        InitializeComponent();
        DataContext = _vm;

        ThemeManager.Apply(_vm.CurrentTheme, System.Windows.Application.Current);

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
            ThemeManager.Apply(_vm.CurrentTheme, System.Windows.Application.Current);
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

        await ShowCrashRecoveryIfNeededAsync();
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI event handler must not propagate exceptions.")]
    private async Task ShowCrashRecoveryIfNeededAsync()
    {
        try
        {
            var window = _crashRecoveryWindowFactory();
            await window.ViewModel.LoadAsync(CancellationToken.None);
            if (window.ViewModel.HasPendingDocuments)
            {
                window.Owner = this;
                window.ShowDialog();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Crash recovery check failed.");
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

    private void OnImportLicenseMenuItemClick(object sender, RoutedEventArgs e)
    {
        var window = _licenseWindowFactory();
        window.Owner = this;
        window.ShowDialog();
    }

    private void OnExitMenuItemClick(object sender, RoutedEventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
    }

    // Reports the page viewport size to the active tab so Fit Width / Fit Page can compute zoom.
    private void OnPageAreaSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DocumentTabViewModel tab })
        {
            tab.SetViewport(e.NewSize.Width, e.NewSize.Height);
        }
    }

    // Multi-page (continuous/two-page) pages render lazily: render when the item is realized,
    // drop the bitmap when it is virtualized out, so memory stays bounded to on-screen pages.
    private void OnVisiblePageLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RenderedPageViewModel page })
        {
            _ = page.EnsureRenderedAsync(CancellationToken.None);
        }
    }

    private void OnVisiblePageUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RenderedPageViewModel page })
        {
            page.Invalidate();
        }
    }
}
