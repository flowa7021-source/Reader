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
    private readonly Func<BatchWindow>? _batchWindowFactory;
    private readonly ILogger<MainWindow> _logger;

    public MainWindow(
        MainViewModel vm,
        Func<SettingsWindow> settingsWindowFactory,
        Func<CrashRecoveryWindow> crashRecoveryWindowFactory,
        Func<LicenseImportWindow> licenseWindowFactory,
        ILogger<MainWindow> logger,
        Func<BatchWindow>? batchWindowFactory = null)
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
        _batchWindowFactory = batchWindowFactory;
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
    private async void OnExportAllPagesAsImagesMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedTab is not { CanExportAllPagesAsImages: true } tab)
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        var dialog = new OpenFolderDialog
        {
            Title = loc["ExportAllPagesDialogTitle"],
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await tab.ExportAllPagesAsImagesCommand.ExecuteAsync(dialog.FolderName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error exporting all pages to '{Path}'.", dialog.FolderName);
            MessageBox.Show(this, ex.Message, loc["ErrorDialogTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI event handler must not propagate exceptions.")]
    private async void OnExportPageImageMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedTab is not { CanExportCurrentPageAsImage: true } tab)
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        var dialog = new SaveFileDialog
        {
            Title = loc["ExportPageImageDialogTitle"],
            Filter = loc["ExportPageImageDialogFilter"],
            FileName = $"{Path.GetFileNameWithoutExtension(tab.FilePath)}-p{tab.CurrentPageIndex + 1}.png",
            DefaultExt = "png",
            AddExtension = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await tab.ExportCurrentPageAsImageCommand.ExecuteAsync(dialog.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error exporting page image to '{Path}'.", dialog.FileName);
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

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI event handler must not propagate exceptions.")]
    private void OnBatchMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (_batchWindowFactory is null)
        {
            return;
        }

        try
        {
            var batchWin = _batchWindowFactory();
            batchWin.Owner = this;
            batchWin.Show();
        }
        catch (Exception ex)
        {
            var loc = LocalizationManager.Instance;
            _logger.LogError(ex, "Failed to open batch window.");
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

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI event handler must not propagate exceptions.")]
    private void OnFillFormMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedTab is not { CanFillForm: true } tab)
        {
            return;
        }

        try
        {
            var dialog = new FormFillDialog(tab) { Owner = this };
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            var loc = LocalizationManager.Instance;
            _logger.LogWarning(ex, "Failed to open form-fill dialog.");
            MessageBox.Show(this, ex.Message, loc["ErrorDialogTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI event handler must not propagate exceptions.")]
    private void OnViewSignaturesMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedTab is not { CanViewSignatures: true } tab)
        {
            return;
        }

        try
        {
            var dialog = new SignaturesDialog(tab) { Owner = this };
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            var loc = LocalizationManager.Instance;
            _logger.LogWarning(ex, "Failed to open signatures dialog.");
            MessageBox.Show(this, ex.Message, loc["ErrorDialogTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnExitMenuItemClick(object sender, RoutedEventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
    }

    /// <summary>Stamp tool: pick an image to use as the next image-stamp (B1e). Cancel keeps the
    /// existing path (use the Clear button to revert to text-stamp).</summary>
    private void OnPickStampImageClick(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedTab is null)
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        var dialog = new OpenFileDialog
        {
            Title = loc["ToolStampPickImageTitle"],
            Filter = loc["ToolStampPickImageFilter"],
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) == true)
        {
            _vm.SelectedTab.SetStampImagePathCommand.Execute(dialog.FileName);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI event handler must not propagate exceptions.")]
    private async void OnExportFormDataMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedTab is not { CanExportFormData: true } tab)
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        var save = new SaveFileDialog
        {
            Title = loc["ExportFormDataDialogTitle"],
            Filter = loc["FormDataDialogFilter"],
            FileName = Path.GetFileNameWithoutExtension(tab.FilePath) + "-form.json",
            DefaultExt = "json",
            AddExtension = true,
        };

        if (save.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await tab.ExportFormDataCommand.ExecuteAsync(save.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error exporting form data to '{Path}'.", save.FileName);
            MessageBox.Show(this, ex.Message, loc["ErrorDialogTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI event handler must not propagate exceptions.")]
    private async void OnImportFormDataMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedTab is not { CanImportFormData: true } tab)
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        var pick = new OpenFileDialog
        {
            Title = loc["ImportFormDataDialogTitle"],
            Filter = loc["FormDataDialogFilter"],
            CheckFileExists = true,
        };

        if (pick.ShowDialog(this) != true)
        {
            return;
        }

        var save = new SaveFileDialog
        {
            Title = loc["ImportFormDataSaveDialogTitle"],
            Filter = loc["ExportAnnotatedPdfDialogFilter"],
            FileName = Path.GetFileNameWithoutExtension(tab.FilePath) + "-filled.pdf",
            DefaultExt = "pdf",
            AddExtension = true,
        };

        if (save.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await tab.ImportFormDataCommand.ExecuteAsync(new Foliant.ViewModels.ImportFormDataRequest(pick.FileName, save.FileName));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error importing form data from '{Source}' into '{Target}'.", pick.FileName, save.FileName);
            MessageBox.Show(this, ex.Message, loc["ErrorDialogTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI event handler must not propagate exceptions.")]
    private async void OnMergePdfsMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (!_vm.CanMergePdfs)
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        var pick = new OpenFileDialog
        {
            Title = loc["MergePdfsPickDialogTitle"],
            Filter = loc["MergeSourcesDialogFilter"],
            Multiselect = true,
        };

        if (pick.ShowDialog(this) != true || pick.FileNames.Length < 2)
        {
            return;
        }

        var save = new SaveFileDialog
        {
            Title = loc["MergePdfsSaveDialogTitle"],
            Filter = loc["ExportAnnotatedPdfDialogFilter"],
            FileName = "merged.pdf",
            DefaultExt = "pdf",
            AddExtension = true,
        };

        if (save.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await _vm.MergePdfsCommand.ExecuteAsync(new Foliant.ViewModels.MergePdfsRequest(pick.FileNames, save.FileName));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error merging PDFs into '{Path}'.", save.FileName);
            MessageBox.Show(this, ex.Message, loc["ErrorDialogTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI event handler must not propagate exceptions.")]
    private async void OnAddWatermarkMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedTab is not { CanAddWatermark: true } tab)
        {
            return;
        }

        var spec = WatermarkDialog.Prompt(this);
        if (spec is null)
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        var save = new SaveFileDialog
        {
            Title = loc["AddWatermarkSaveDialogTitle"],
            Filter = loc["ExportAnnotatedPdfDialogFilter"],
            FileName = Path.GetFileNameWithoutExtension(tab.FilePath) + "-watermarked.pdf",
            DefaultExt = "pdf",
            AddExtension = true,
        };

        if (save.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await tab.ApplyWatermarkCommand.ExecuteAsync(new Foliant.ViewModels.ApplyWatermarkRequest(spec, save.FileName));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error applying watermark to '{Path}'.", save.FileName);
            MessageBox.Show(this, ex.Message, loc["ErrorDialogTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI event handler must not propagate exceptions.")]
    private async void OnAddHeaderFooterMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedTab is not { CanAddHeaderFooter: true } tab)
        {
            return;
        }

        var spec = HeaderFooterDialog.Prompt(this);
        if (spec is null)
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        var save = new SaveFileDialog
        {
            Title = loc["AddHeaderFooterSaveDialogTitle"],
            Filter = loc["ExportAnnotatedPdfDialogFilter"],
            FileName = Path.GetFileNameWithoutExtension(tab.FilePath) + "-headerfooter.pdf",
            DefaultExt = "pdf",
            AddExtension = true,
        };

        if (save.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await tab.ApplyHeaderFooterCommand.ExecuteAsync(new Foliant.ViewModels.ApplyHeaderFooterRequest(spec, save.FileName));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error applying header/footer to '{Path}'.", save.FileName);
            MessageBox.Show(this, ex.Message, loc["ErrorDialogTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI event handler must not propagate exceptions.")]
    private async void OnCropPagesMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedTab is not { CanCropPages: true } tab)
        {
            return;
        }

        var spec = CropDialog.Prompt(this);
        if (spec is null)
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        var save = new SaveFileDialog
        {
            Title = loc["CropPagesSaveDialogTitle"],
            Filter = loc["ExportAnnotatedPdfDialogFilter"],
            FileName = Path.GetFileNameWithoutExtension(tab.FilePath) + "-cropped.pdf",
            DefaultExt = "pdf",
            AddExtension = true,
        };

        if (save.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await tab.CropPagesCommand.ExecuteAsync(new Foliant.ViewModels.CropPagesRequest(spec, save.FileName));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error cropping pages to '{Path}'.", save.FileName);
            MessageBox.Show(this, ex.Message, loc["ErrorDialogTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI event handler must not propagate exceptions.")]
    private async void OnRedactPagesMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedTab is not { CanRedactPages: true } tab)
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        var save = new SaveFileDialog
        {
            Title = loc["RedactionSaveDialogTitle"],
            Filter = loc["ExportAnnotatedPdfDialogFilter"],
            FileName = Path.GetFileNameWithoutExtension(tab.FilePath) + "-redacted.pdf",
            DefaultExt = "pdf",
            AddExtension = true,
        };

        if (save.ShowDialog(this) != true)
        {
            return;
        }

        var request = RedactionDialog.Prompt(this, save.FileName);
        if (request is null)
        {
            return;
        }

        try
        {
            await tab.FindAndRedactCommand.ExecuteAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error redacting matches of '{Query}' to '{Path}'.",
                request.Query, save.FileName);
            MessageBox.Show(this, ex.Message, loc["ErrorDialogTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
