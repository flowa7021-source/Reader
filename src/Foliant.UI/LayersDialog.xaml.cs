using System.Collections.ObjectModel;
using System.Windows;
using Foliant.ViewModels;

namespace Foliant.UI;

/// <summary>
/// Collects a <see cref="SaveLayerVisibilityRequest"/> for OCG layer toggle. The dialog displays one
/// <c>CheckBox</c> per <see cref="PdfLayerViewModel"/> bound two-way on <c>IsVisible</c>; pressing
/// Save returns the per-index visibility map alongside the target path picked by the caller. When
/// the document has no layers the list collapses and an empty-state hint is shown instead.
///
/// <para>Mirrors the self-DataContext + static <c>Prompt</c> shape of
/// <see cref="BatesNumberingDialog"/>. Property names <c>Layers</c>, <c>HasLayers</c>,
/// <c>EmptyHintVisibility</c> and <c>ListVisibility</c> are deliberately not WPF reserved
/// (<see cref="Window"/> / <see cref="System.Windows.Controls.Control"/>) names — Q-F8 follows the
/// W2/W3 lesson on cross-platform CS0108 blind-spots in this WPF-only project.</para>
/// </summary>
public partial class LayersDialog : Window
{
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

    /// <summary>Visibility of the scroll viewer that hosts the layer checkboxes. Inversely paired
    /// with <see cref="EmptyHintVisibility"/>; computed in code-behind because no inverse Bool→Vis
    /// converter is registered at app-scope.</summary>
    public Visibility ListVisibility => HasLayers ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Visibility of the «no layers» hint. The pair stays in sync with <see cref="Layers"/>
    /// because the dialog is configured before <c>ShowDialog</c> and the list is not mutated after.</summary>
    public Visibility EmptyHintVisibility => HasLayers ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Open the dialog modally seeded with <paramref name="layers"/>. Returns
    /// <c>(null, false)</c> on cancel; otherwise a <see cref="SaveLayerVisibilityRequest"/> targeting
    /// <paramref name="defaultTargetPath"/> ready for
    /// <c>DocumentTabViewModel.SaveLayerVisibilityCommand</c>. The map only includes indices whose
    /// visibility actually differs from the source snapshot — keeps the on-wire payload small and
    /// the OCG writer's job minimal.</summary>
    public static (SaveLayerVisibilityRequest? request, bool ok) Prompt(
        Window? owner,
        IReadOnlyList<PdfLayerViewModel> layers,
        string defaultTargetPath)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(defaultTargetPath);

        var dialog = new LayersDialog { Owner = owner };
        var initialVisibility = new Dictionary<int, bool>(layers.Count);
        foreach (var layer in layers)
        {
            dialog.Layers.Add(layer);
            initialVisibility[layer.Index] = layer.IsVisible;
        }

        if (dialog.ShowDialog() != true)
        {
            return (null, false);
        }

        var visibilityByIndex = new Dictionary<int, bool>(layers.Count);
        foreach (var layer in dialog.Layers)
        {
            if (initialVisibility.TryGetValue(layer.Index, out bool was) && was != layer.IsVisible)
            {
                visibilityByIndex[layer.Index] = layer.IsVisible;
            }
        }

        return (new SaveLayerVisibilityRequest(visibilityByIndex, defaultTargetPath), true);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
