using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Foliant.Domain;
using Foliant.ViewModels;

namespace Foliant.UI;

/// <summary>
/// Edits PDF «Initial View» settings (catalog <c>/PageLayout</c>, <c>/PageMode</c> +
/// <c>/ViewerPreferences</c> flags). Pre-filled from the document's current settings; on Save-As it
/// builds a <see cref="SaveViewerPreferencesRequest"/> for
/// <c>DocumentTabViewModel.SaveViewerPreferencesCommand</c>.
///
/// Mirrors <see cref="DocumentPropertiesDialog"/>'s self-DataContext + static <c>Prompt</c> shape.
/// Combo order is kept in explicit arrays so it does not depend on enum integer values.
/// </summary>
public partial class ViewerPreferencesDialog : Window, INotifyPropertyChanged
{
    private static readonly PdfPageLayout[] LayoutByIndex =
    [
        PdfPageLayout.Default,
        PdfPageLayout.SinglePage,
        PdfPageLayout.OneColumn,
        PdfPageLayout.TwoColumnLeft,
        PdfPageLayout.TwoColumnRight,
        PdfPageLayout.TwoPageLeft,
        PdfPageLayout.TwoPageRight,
    ];

    private static readonly PdfPageMode[] ModeByIndex =
    [
        PdfPageMode.Default,
        PdfPageMode.UseNone,
        PdfPageMode.UseOutlines,
        PdfPageMode.UseThumbs,
        PdfPageMode.FullScreen,
        PdfPageMode.UseOC,
        PdfPageMode.UseAttachments,
    ];

    public event PropertyChangedEventHandler? PropertyChanged;

    private int _pageLayoutIndex;
    private int _pageModeIndex;
    private bool _hideToolbar;
    private bool _hideMenubar;
    private bool _fitWindow;
    private bool _centerWindow;
    private bool _displayDocTitle;

    public ViewerPreferencesDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    /// <summary>Selected page-layout index into <see cref="LayoutByIndex"/> (combo order).</summary>
    public int PageLayoutIndex { get => _pageLayoutIndex; set { _pageLayoutIndex = value; Notify(); } }

    /// <summary>Selected page-mode index into <see cref="ModeByIndex"/> (combo order).</summary>
    public int PageModeIndex { get => _pageModeIndex; set { _pageModeIndex = value; Notify(); } }

    /// <summary>Hide the viewer's toolbar on open (<c>/HideToolbar</c>).</summary>
    public bool HideToolbar { get => _hideToolbar; set { _hideToolbar = value; Notify(); } }

    /// <summary>Hide the viewer's menu bar on open (<c>/HideMenubar</c>).</summary>
    public bool HideMenubar { get => _hideMenubar; set { _hideMenubar = value; Notify(); } }

    /// <summary>Resize the viewer window to the first page (<c>/FitWindow</c>).</summary>
    public bool FitWindow { get => _fitWindow; set { _fitWindow = value; Notify(); } }

    /// <summary>Centre the viewer window on screen (<c>/CenterWindow</c>).</summary>
    public bool CenterWindow { get => _centerWindow; set { _centerWindow = value; Notify(); } }

    /// <summary>Show the document <c>/Title</c> in the window title bar (<c>/DisplayDocTitle</c>).</summary>
    public bool DisplayDocTitle { get => _displayDocTitle; set { _displayDocTitle = value; Notify(); } }

    /// <summary>Open the dialog modally, pre-filled from <paramref name="current"/>. Returns
    /// <c>(null, false)</c> on cancel, otherwise a <see cref="SaveViewerPreferencesRequest"/> with the
    /// chosen settings targeting <paramref name="defaultTargetPath"/>.</summary>
    /// <param name="owner">Owner window for centring.</param>
    /// <param name="current">Existing settings to pre-fill.</param>
    /// <param name="defaultTargetPath">Save-As target chosen by the caller.</param>
    public static (SaveViewerPreferencesRequest? request, bool ok) Prompt(
        Window? owner, PdfViewerPreferences current, string defaultTargetPath)
    {
        ArgumentNullException.ThrowIfNull(current);

        var dialog = new ViewerPreferencesDialog
        {
            Owner = owner,
            PageLayoutIndex = Math.Max(0, Array.IndexOf(LayoutByIndex, current.PageLayout)),
            PageModeIndex = Math.Max(0, Array.IndexOf(ModeByIndex, current.PageMode)),
            HideToolbar = current.HideToolbar,
            HideMenubar = current.HideMenubar,
            FitWindow = current.FitWindow,
            CenterWindow = current.CenterWindow,
            DisplayDocTitle = current.DisplayDocTitle,
        };

        if (dialog.ShowDialog() != true)
        {
            return (null, false);
        }

        var prefs = new PdfViewerPreferences(
            LayoutByIndex[Math.Clamp(dialog.PageLayoutIndex, 0, LayoutByIndex.Length - 1)],
            ModeByIndex[Math.Clamp(dialog.PageModeIndex, 0, ModeByIndex.Length - 1)],
            dialog.HideToolbar,
            dialog.HideMenubar,
            dialog.FitWindow,
            dialog.CenterWindow,
            dialog.DisplayDocTitle);
        return (new SaveViewerPreferencesRequest(prefs, defaultTargetPath), true);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
