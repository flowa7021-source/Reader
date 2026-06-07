using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Foliant.Domain;
using Foliant.ViewModels;

namespace Foliant.UI;

/// <summary>
/// Collects outline richness options for «Export Bookmarks to PDF»: destination zoom mode, whether to
/// write parents collapsed, and whether to bold top-level entries. On Export it builds an
/// <see cref="ExportOutlineRequest"/> for <c>DocumentTabViewModel.ExportBookmarksToPdfCommand</c>.
///
/// <para>Mirrors <see cref="PageLabelsDialog"/>'s self-DataContext + static <c>Prompt</c> shape.
/// Property names avoid <see cref="Window"/> members to dodge CS0108.</para>
/// </summary>
public partial class OutlineExportOptionsDialog : Window, INotifyPropertyChanged
{
    // Combo order ↔ enum.
    private static readonly OutlineDestinationMode[] DestByIndex =
    [
        OutlineDestinationMode.FitPage,
        OutlineDestinationMode.FitWidth,
        OutlineDestinationMode.FitHeight,
        OutlineDestinationMode.InheritZoom,
    ];

    public event PropertyChangedEventHandler? PropertyChanged;

    private int _destinationIndex;
    private bool _collapseNested;
    private bool _boldTopLevel;

    public OutlineExportOptionsDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    /// <summary>Selected destination-mode index into <see cref="DestByIndex"/> (combo order).</summary>
    public int DestinationIndex { get => _destinationIndex; set { _destinationIndex = value; Notify(); } }

    /// <summary>Write parent nodes collapsed (negative <c>/Count</c>) so the outline opens compact.</summary>
    public bool CollapseNested { get => _collapseNested; set { _collapseNested = value; Notify(); } }

    /// <summary>Render top-level (depth-0) entries bold.</summary>
    public bool BoldTopLevel { get => _boldTopLevel; set { _boldTopLevel = value; Notify(); } }

    /// <summary>Open the dialog modally. Returns <c>(null, false)</c> on cancel, otherwise an
    /// <see cref="ExportOutlineRequest"/> targeting <paramref name="defaultTargetPath"/>.</summary>
    /// <param name="owner">Owner window for centring.</param>
    /// <param name="defaultTargetPath">Save-As target chosen by the caller.</param>
    public static (ExportOutlineRequest? request, bool ok) Prompt(Window? owner, string defaultTargetPath)
    {
        ArgumentNullException.ThrowIfNull(defaultTargetPath);

        var dialog = new OutlineExportOptionsDialog { Owner = owner };
        if (dialog.ShowDialog() != true)
        {
            return (null, false);
        }

        var mode = DestByIndex[Math.Clamp(dialog.DestinationIndex, 0, DestByIndex.Length - 1)];
        return (new ExportOutlineRequest(defaultTargetPath, mode, dialog.CollapseNested, dialog.BoldTopLevel), true);
    }

    private void OnExportClick(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
