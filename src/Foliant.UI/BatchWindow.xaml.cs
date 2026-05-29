using System.Diagnostics.CodeAnalysis;
using System.Windows;
using Foliant.Domain;
using Foliant.UI.Localization;
using Foliant.ViewModels;
using Microsoft.Win32;

namespace Foliant.UI;

/// <summary>
/// Window for Q-F29 batch processing. Hosts <see cref="BatchViewModel"/>; the dialogs for
/// per-operation specs (Watermark / HeaderFooter / Crop) are reused from the existing File-menu
/// flow, keeping the spec-collection logic in one place.
/// </summary>
public partial class BatchWindow : Window
{
    private readonly BatchViewModel _vm;

    public BatchWindow(BatchViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        _vm = vm;
        InitializeComponent();
        DataContext = _vm;
    }

    private void OnFilesDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFilesDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            // Only PDFs make sense for the current operation set — silently filter the rest.
            var pdfs = paths.Where(p => p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)).ToArray();
            _vm.AddFilesCommand.Execute(pdfs);
        }
        e.Handled = true;
    }

    private void OnAddFilesClick(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        var dialog = new OpenFileDialog
        {
            Title = loc["BatchAddDialogTitle"],
            Filter = loc["ExportAnnotatedPdfDialogFilter"],
            Multiselect = true,
        };

        if (dialog.ShowDialog(this) == true)
        {
            _vm.AddFilesCommand.Execute(dialog.FileNames);
        }
    }

    private void OnBrowseOutputClick(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        var dialog = new OpenFolderDialog
        {
            Title = loc["BatchBrowseDialogTitle"],
        };

        if (dialog.ShowDialog(this) == true)
        {
            _vm.OutputFolder = dialog.FolderName;
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI event handler must not propagate exceptions.")]
    private void OnConfigureClick(object sender, RoutedEventArgs e)
    {
        try
        {
            object? spec = _vm.Operation switch
            {
                BatchOperationKind.Watermark => WatermarkDialog.Prompt(this),
                BatchOperationKind.HeaderFooter => HeaderFooterDialog.Prompt(this),
                BatchOperationKind.Crop => CropDialog.Prompt(this),
                _ => null,
            };
            if (spec is not null)
            {
                _vm.CurrentSpec = spec;
            }
        }
        catch (Exception ex)
        {
            var loc = LocalizationManager.Instance;
            MessageBox.Show(this, ex.Message, loc["ErrorDialogTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _vm.Dispose();
    }
}
