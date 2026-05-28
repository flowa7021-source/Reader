using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI event handler must not propagate exceptions.")]
    private async void OnExportAnnotatedPdfMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedTab is not { CanExportAnnotatedPdf: true } tab)
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        var dialog = new SaveFileDialog
        {
            Title = loc["ExportAnnotatedPdfDialogTitle"],
            Filter = loc["ExportAnnotatedPdfDialogFilter"],
            FileName = Path.GetFileNameWithoutExtension(tab.FilePath) + "-annotated.pdf",
            DefaultExt = "pdf",
            AddExtension = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await tab.ExportAnnotatedPdfCommand.ExecuteAsync(dialog.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error exporting annotated PDF to '{Path}'.", dialog.FileName);
            MessageBox.Show(this, ex.Message, loc["ErrorDialogTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI event handler must not propagate exceptions.")]
    private async void OnExportBookmarksMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedTab is not { CanExportBookmarks: true } tab)
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        var dialog = new SaveFileDialog
        {
            Title = loc["ExportBookmarksDialogTitle"],
            Filter = loc["BookmarksDialogFilter"],
            FileName = Path.GetFileNameWithoutExtension(tab.FilePath) + "-bookmarks.json",
            DefaultExt = "json",
            AddExtension = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await tab.ExportBookmarksCommand.ExecuteAsync(dialog.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error exporting bookmarks to '{Path}'.", dialog.FileName);
            MessageBox.Show(this, ex.Message, loc["ErrorDialogTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI event handler must not propagate exceptions.")]
    private async void OnImportBookmarksMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedTab is not { CanImportBookmarks: true } tab)
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        var dialog = new OpenFileDialog
        {
            Title = loc["ImportBookmarksDialogTitle"],
            Filter = loc["BookmarksDialogFilter"],
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await tab.ImportBookmarksCommand.ExecuteAsync(dialog.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error importing bookmarks from '{Path}'.", dialog.FileName);
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

    // Thumbnails render lazily when their strip item is realized; they persist (small, cached).
    private void OnThumbnailLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PageThumbnailViewModel page })
        {
            _ = page.EnsureThumbnailAsync(CancellationToken.None);
        }
    }

    // ── Thumbnail strip drag-and-drop reorder ──
    private Point _thumbDragStart;
    private PageThumbnailViewModel? _thumbDragItem;

    private void OnThumbnailStripPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _thumbDragStart = e.GetPosition(null);
        _thumbDragItem = ThumbnailUnder(e.OriginalSource as DependencyObject);
    }

    private void OnThumbnailStripMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _thumbDragItem is null)
        {
            return;
        }

        Vector moved = _thumbDragStart - e.GetPosition(null);
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop((DependencyObject)sender, _thumbDragItem, DragDropEffects.Move);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Drop handler must not propagate; reorder failures already surface as the tab's ErrorMessage.")]
    private async void OnThumbnailStripDrop(object sender, DragEventArgs e)
    {
        try
        {
            if (e.Data.GetData(typeof(PageThumbnailViewModel)) is not PageThumbnailViewModel source ||
                ThumbnailUnder(e.OriginalSource as DependencyObject) is not { } target ||
                ReferenceEquals(source, target) ||
                ((FrameworkElement)sender).DataContext is not DocumentTabViewModel tab)
            {
                return;
            }

            await tab.Thumbnails.MoveAsync(source.PageIndex, target.PageIndex, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Thumbnail drag-reorder failed.");
        }
        finally
        {
            _thumbDragItem = null;
        }
    }

    private static PageThumbnailViewModel? ThumbnailUnder(DependencyObject? origin)
    {
        DependencyObject? current = origin;
        while (current is not null and not ListBoxItem)
        {
            current = VisualTreeHelper.GetParent(current);
        }
        return (current as ListBoxItem)?.DataContext as PageThumbnailViewModel;
    }

    private void OnBookmarkRowClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Row — Border; его Tag — DocumentTabViewModel (через AncestorType=ItemsControl),
        // DataContext — конкретный Bookmark. Прыгаем через VM-команду, чтобы пройти всю
        // навигационную обвязку (history, multi-page sync).
        if (sender is FrameworkElement { Tag: DocumentTabViewModel tab, DataContext: Foliant.Domain.Bookmark bm })
        {
            tab.JumpToBookmarkCommand.Execute(bm);
        }
    }

    private void OnBookmarkRenameClick(object sender, RoutedEventArgs e)
    {
        if (!TryResolveContextTarget<Foliant.Domain.Bookmark>(sender, out var tab, out var bm))
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        string? input = InputDialog.Prompt(this, loc["BookmarkRenameDialogTitle"], loc["BookmarkRenameDialogLabel"], bm.Label);
        if (input is null)
        {
            return;
        }

        string trimmed = input.Trim();
        if (string.IsNullOrEmpty(trimmed) || string.Equals(trimmed, bm.Label, StringComparison.Ordinal))
        {
            return;
        }

        tab.RenameBookmarkCommand.Execute(new RenameBookmarkRequest(bm, trimmed));
    }

    private void OnAnnotationEditSubjectClick(object sender, RoutedEventArgs e)
    {
        if (!TryResolveContextTarget<Foliant.Domain.Annotation>(sender, out var tab, out var annotation))
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        string? input = InputDialog.Prompt(
            this,
            loc["AnnotationEditSubjectDialogTitle"],
            loc["AnnotationEditSubjectDialogLabel"],
            annotation.Subject ?? string.Empty);

        if (input is null)
        {
            return;
        }

        tab.EditNoteSubjectCommand.Execute((annotation, (string?)input));
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI event handler must not propagate exceptions.")]
    private async void OnBookmarkExtractClick(object sender, RoutedEventArgs e)
    {
        // Извлекаем VM + Bookmark через те же пути, что и rename/delete контекст: MenuItem.PlacementTarget = Border row.
        if (sender is not MenuItem { CommandParameter: Foliant.Domain.Bookmark bm } mi
            || mi.Parent is not ContextMenu cm
            || cm.PlacementTarget is not FrameworkElement host
            || host.Tag is not DocumentTabViewModel tab
            || !tab.CanExtractPagesFromBookmark)
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        var dialog = new SaveFileDialog
        {
            Title = loc["ExtractPagesDialogTitle"],
            Filter = loc["ExportAnnotatedPdfDialogFilter"],   // тот же фильтр "PDF files (*.pdf)|*.pdf" — экономим строку.
            FileName = Path.GetFileNameWithoutExtension(tab.FilePath) + " - " + SanitizeFileName(bm.Label) + ".pdf",
            DefaultExt = "pdf",
            AddExtension = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await tab.ExtractPagesFromBookmarkCommand.ExecuteAsync(new ExtractBookmarkRangeRequest(bm, dialog.FileName));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error extracting pages from bookmark '{Label}' → '{Target}'.", bm.Label, dialog.FileName);
            MessageBox.Show(this, ex.Message, loc["ErrorDialogTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // CommandParameter — сам элемент (Bookmark/Annotation); VM достаём через PlacementTarget.Tag,
    // как и остальные context-menu обработчики в этом окне.
    private static bool TryResolveContextTarget<T>(object sender, out DocumentTabViewModel tab, out T target)
        where T : class
    {
        tab = null!;
        target = null!;
        if (sender is MenuItem { CommandParameter: T item } mi
            && mi.Parent is ContextMenu cm
            && cm.PlacementTarget is FrameworkElement host
            && host.Tag is DocumentTabViewModel resolvedTab)
        {
            tab = resolvedTab;
            target = item;
            return true;
        }

        return false;
    }

    private static string SanitizeFileName(string label)
    {
        // SaveFileDialog отбрасывает невалидные символы тихо, но мы заранее убираем,
        // чтобы дефолтное имя выглядело осмысленно.
        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(label.Length);
        foreach (char c in label)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }
        return sb.ToString();
    }
}
