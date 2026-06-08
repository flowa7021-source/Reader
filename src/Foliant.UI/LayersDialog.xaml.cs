using System.Collections.ObjectModel;
using System.Windows;
using Foliant.UI.Localization;
using Foliant.ViewModels;

namespace Foliant.UI;

/// <summary>
/// Manages OCG layers («PDF layers»): per-layer visibility toggle (batched + saved together) plus
/// per-layer Rename and Delete actions (each a single mutate-to-copy operation). The dialog displays
/// one row per <see cref="PdfLayerViewModel"/> — a two-way <c>IsVisible</c> checkbox and Rename/Delete
/// buttons. It returns a <see cref="LayersDialogOutcome"/> describing the single action the user chose,
/// with its request envelope already targeting the caller-picked path. When the document has no layers
/// the list collapses and an empty-state hint is shown instead.
///
/// <para>Mirrors the self-DataContext + static <c>Prompt</c> shape of <see cref="BatesNumberingDialog"/>.
/// Property names <c>Layers</c>, <c>HasLayers</c>, <c>EmptyHintVisibility</c> and <c>ListVisibility</c>
/// are deliberately not WPF reserved (<see cref="Window"/> / <see cref="System.Windows.Controls.Control"/>)
/// names — Q-F8 follows the W2/W3 lesson on cross-platform CS0108 blind-spots in this WPF-only project.</para>
/// </summary>
public partial class LayersDialog : Window
{
    private readonly Dictionary<int, bool> _initialVisibility = new();
    private string _targetPath = string.Empty;
    private LayersDialogOutcome _outcome = LayersDialogOutcome.Cancelled;

    public LayersDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    /// <summary>Bound to the dialog's <c>ItemsControl.ItemsSource</c>. Populated by
    /// <see cref="Prompt"/> before the modal is shown so layout sees the final count.</summary>
    public ObservableCollection<PdfLayerViewModel> Layers { get; } = new();

    /// <summary>True when at least one layer was loaded — drives Save enablement and toggles the
    /// list/empty-hint visibility pair.</summary>
    public bool HasLayers => Layers.Count > 0;

    /// <summary>Visibility of the scroll viewer that hosts the layer rows. Inversely paired
    /// with <see cref="EmptyHintVisibility"/>; computed in code-behind because no inverse Bool→Vis
    /// converter is registered at app-scope.</summary>
    public Visibility ListVisibility => HasLayers ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Visibility of the «no layers» hint. The pair stays in sync with <see cref="Layers"/>
    /// because the dialog is configured before <c>ShowDialog</c> and the list is not mutated after.</summary>
    public Visibility EmptyHintVisibility => HasLayers ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Open the dialog modally seeded with <paramref name="layers"/>. Returns
    /// <see cref="LayersDialogOutcome.Cancelled"/> on cancel; otherwise an outcome carrying the single
    /// action the user chose (visibility save / rename / delete) with a request envelope targeting
    /// <paramref name="defaultTargetPath"/>, ready for the matching <c>DocumentTabViewModel</c> command.
    /// For a visibility save the map only includes indices whose state actually changed — keeps the
    /// on-wire payload small and the OCG writer's job minimal.</summary>
    public static LayersDialogOutcome Prompt(
        Window? owner,
        IReadOnlyList<PdfLayerViewModel> layers,
        string defaultTargetPath)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(defaultTargetPath);

        var dialog = new LayersDialog { Owner = owner, _targetPath = defaultTargetPath };
        foreach (var layer in layers)
        {
            dialog.Layers.Add(layer);
            dialog._initialVisibility[layer.Index] = layer.IsVisible;
        }

        return dialog.ShowDialog() == true ? dialog._outcome : LayersDialogOutcome.Cancelled;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var visibilityByIndex = new Dictionary<int, bool>(Layers.Count);
        foreach (var layer in Layers)
        {
            if (_initialVisibility.TryGetValue(layer.Index, out bool was) && was != layer.IsVisible)
            {
                visibilityByIndex[layer.Index] = layer.IsVisible;
            }
        }

        _outcome = new LayersDialogOutcome(
            LayersDialogAction.SetVisibility,
            Visibility: new SaveLayerVisibilityRequest(visibilityByIndex, _targetPath));
        DialogResult = true;
    }

    private void OnRenameLayerClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: PdfLayerViewModel layer })
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        string? newName = InputDialog.Prompt(this, loc["LayersRenameDialogTitle"], loc["LayersRenameLabel"], layer.LayerName);
        if (string.IsNullOrWhiteSpace(newName))
        {
            return; // cancelled or empty — keep the dialog open, no rename
        }

        _outcome = new LayersDialogOutcome(
            LayersDialogAction.Rename,
            Rename: new RenameLayerRequest(layer.Index, newName.Trim(), _targetPath));
        DialogResult = true;
    }

    private void OnDeleteLayerClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: PdfLayerViewModel layer })
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        string message = loc.Format("LayersDeleteConfirmMessage", layer.LayerName);
        if (MessageBox.Show(this, message, loc["LayersDeleteConfirmTitle"], MessageBoxButton.YesNo, MessageBoxImage.Warning)
            != MessageBoxResult.Yes)
        {
            return; // declined — keep the dialog open
        }

        _outcome = new LayersDialogOutcome(
            LayersDialogAction.Delete,
            Delete: new DeleteLayerRequest(layer.Index, _targetPath));
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}

/// <summary>The single action a user committed in <see cref="LayersDialog"/>.</summary>
public enum LayersDialogAction
{
    /// <summary>Dialog cancelled — no action.</summary>
    Cancel,

    /// <summary>Save the per-layer visibility map (<see cref="LayersDialogOutcome.Visibility"/>).</summary>
    SetVisibility,

    /// <summary>Rename one layer (<see cref="LayersDialogOutcome.Rename"/>).</summary>
    Rename,

    /// <summary>Delete one layer (<see cref="LayersDialogOutcome.Delete"/>).</summary>
    Delete,
}

/// <summary>Discriminated result of <see cref="LayersDialog.Prompt"/>: exactly one of the request
/// envelopes is non-null, selected by <see cref="Action"/>. Defined in the UI layer because it is a
/// View-flow concern; the request records it carries live in <c>Foliant.ViewModels</c>.</summary>
/// <param name="Action">Which action the user chose.</param>
/// <param name="Visibility">Visibility-save request when <paramref name="Action"/> is
/// <see cref="LayersDialogAction.SetVisibility"/>.</param>
/// <param name="Rename">Rename request when <paramref name="Action"/> is
/// <see cref="LayersDialogAction.Rename"/>.</param>
/// <param name="Delete">Delete request when <paramref name="Action"/> is
/// <see cref="LayersDialogAction.Delete"/>.</param>
public sealed record LayersDialogOutcome(
    LayersDialogAction Action,
    SaveLayerVisibilityRequest? Visibility = null,
    RenameLayerRequest? Rename = null,
    DeleteLayerRequest? Delete = null)
{
    /// <summary>Shared "user cancelled" sentinel.</summary>
    public static LayersDialogOutcome Cancelled { get; } = new(LayersDialogAction.Cancel);
}
