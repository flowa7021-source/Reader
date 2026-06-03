using CommunityToolkit.Mvvm.ComponentModel;
using Foliant.Domain;

namespace Foliant.ViewModels;

/// <summary>
/// Mutable view-model wrapper around an immutable <see cref="PdfLayer"/> snapshot so the layers
/// dialog can two-way bind a <c>CheckBox</c> to per-layer visibility without mutating the domain
/// record. <see cref="Index"/> is the stable identifier carried from the source PDF — passed back
/// into <c>IPdfOcgService.SetLayerVisibilityAsync</c> as the dictionary key when the user saves a
/// modified copy.
///
/// <para>Property name <c>LayerName</c> (not <c>Name</c>) is deliberate: were a XAML author to bind
/// a layer to a <c>FrameworkElement</c> directly, <c>Name</c> would collide with
/// <c>FrameworkElement.Name</c>. <see cref="IsVisible"/> is an ObservableObject-generated property,
/// not a Window member, so it is safe.</para>
/// </summary>
public sealed partial class PdfLayerViewModel : ObservableObject
{
    public int Index { get; }

    public string LayerName { get; }

    [ObservableProperty]
    private bool _isVisible;

    public PdfLayerViewModel(PdfLayer source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Index = source.Index;
        LayerName = source.Name;
        _isVisible = source.IsVisible;
    }
}
