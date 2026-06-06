using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using Foliant.Domain;

namespace Foliant.UI;

/// <summary>The action the user chose in <see cref="NamedDestinationsDialog"/>.</summary>
public enum NamedDestinationActionKind
{
    /// <summary>No action (dialog closed).</summary>
    None,

    /// <summary>Add/replace a destination (name + target page from the form).</summary>
    Add,

    /// <summary>Remove the selected destination.</summary>
    Remove,
}

/// <summary>
/// Lists named destinations and lets the user add (name + target page) or remove one, returning that
/// choice; the View performs the Save-As and forwards to the matching VM command (dialog touches no
/// services, mirroring <see cref="AttachmentsDialog"/>). Add uses the form fields; Remove uses the
/// selected row.
/// </summary>
public partial class NamedDestinationsDialog : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private int _pageCount = 1;
    private string _nameText = string.Empty;
    private string _pageText = "1";
    private NamedDestinationRow? _selectedRow;

    public NamedDestinationsDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    /// <summary>The destination rows shown in the list.</summary>
    public ObservableCollection<NamedDestinationRow> Rows { get; } = [];

    /// <summary>The currently selected row (or null).</summary>
    public NamedDestinationRow? SelectedRow { get => _selectedRow; set { _selectedRow = value; Notify(); } }

    /// <summary>Name of the destination being added.</summary>
    public string NameText { get => _nameText; set { _nameText = value; Notify(); } }

    /// <summary>1-based target page of the destination being added.</summary>
    public string PageText { get => _pageText; set { _pageText = value; Notify(); } }

    /// <summary>The chosen action; valid only when <c>ShowDialog</c> returned true.</summary>
    public NamedDestinationActionKind ChosenAction { get; private set; } = NamedDestinationActionKind.None;

    /// <summary>The name for the chosen action (form name for Add, selected name for Remove).</summary>
    public string ResultName { get; private set; } = string.Empty;

    /// <summary>The 0-based target page index for an Add action.</summary>
    public int ResultPageIndex { get; private set; }

    /// <summary>Open the dialog modally, listing <paramref name="current"/>. Returns the chosen action +
    /// name + 0-based page index (for Add) + ok=false on Close.</summary>
    /// <param name="owner">Owner window for centring.</param>
    /// <param name="current">Existing destinations.</param>
    /// <param name="pageCount">Document page count, used to bound the target page.</param>
    public static (NamedDestinationActionKind action, string name, int pageIndex, bool ok) Prompt(
        Window? owner, IReadOnlyList<PdfNamedDestination> current, int pageCount)
    {
        ArgumentNullException.ThrowIfNull(current);

        var dialog = new NamedDestinationsDialog { Owner = owner };
        dialog._pageCount = Math.Max(1, pageCount);
        foreach (var d in current)
        {
            dialog.Rows.Add(NamedDestinationRow.From(d));
        }

        dialog.EmptyLabel.Visibility = dialog.Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (dialog.ShowDialog() != true)
        {
            return (NamedDestinationActionKind.None, string.Empty, 0, false);
        }

        return (dialog.ChosenAction, dialog.ResultName, dialog.ResultPageIndex, true);
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameText))
        {
            return;
        }

        int page1 = TryParseInt(PageText, out int p) ? Math.Clamp(p, 1, _pageCount) : 1;
        ResultName = NameText.Trim();
        ResultPageIndex = page1 - 1;
        ChosenAction = NamedDestinationActionKind.Add;
        DialogResult = true;
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is null)
        {
            return;
        }

        ResultName = SelectedRow.Destination.Name;
        ChosenAction = NamedDestinationActionKind.Remove;
        DialogResult = true;
    }

    private static bool TryParseInt(string text, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value)
        || int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private void OnCloseClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One list row: a named destination plus a display line («name → p.N»).</summary>
public sealed class NamedDestinationRow
{
    private NamedDestinationRow(PdfNamedDestination destination, string display)
    {
        Destination = destination;
        Display = display;
    }

    /// <summary>The destination this row represents.</summary>
    public PdfNamedDestination Destination { get; }

    /// <summary>Human-readable line shown in the list.</summary>
    public string Display { get; }

    /// <summary>Build a row from a destination.</summary>
    /// <param name="destination">The destination to describe.</param>
    public static NamedDestinationRow From(PdfNamedDestination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        string display = string.Create(CultureInfo.CurrentCulture, $"{destination.Name}  →  p.{destination.PageIndex + 1}");
        return new NamedDestinationRow(destination, display);
    }
}
