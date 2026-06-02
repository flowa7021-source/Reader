using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;

namespace Foliant.UI;

/// <summary>
/// Collects a 1-based page-range string (e.g. <c>"1-3,7,10-12"</c>) and converts it to an ordered,
/// de-duplicated list of 0-based indices for <c>DocumentTabViewModel.ExtractSelectionCommand</c>.
/// Mirrors <see cref="CropDialog"/>'s self-DataContext + static <see cref="Prompt"/> shape. The
/// property is named <c>SelectionText</c> (not a <see cref="Window"/> member). Validation is bound
/// to the constructor's <c>pageCount</c> so OK stays disabled until every referenced page exists.
/// </summary>
public partial class ExtractSelectionDialog : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly int _pageCount;
    private string _selectionText = string.Empty;

    public ExtractSelectionDialog(int pageCount)
    {
        _pageCount = pageCount;
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) => { RangeBox.Focus(); };
    }

    /// <summary>Raw 1-based range string so partial/invalid input stays editable; parsed on validate/OK.</summary>
    public string SelectionText { get => _selectionText; set { _selectionText = value; Notify(); Notify(nameof(IsValid)); } }

    /// <summary>OK enabled only when the string parses to a non-empty selection with every page in
    /// <c>[1, pageCount]</c>.</summary>
    public bool IsValid => TryExpand(out _);

    /// <summary>Open the dialog modally. Returns <c>(null, false)</c> on cancel/invalid input,
    /// otherwise the ordered 0-based page indices.</summary>
    public static (IReadOnlyList<int>? pages, bool ok) Prompt(Window? owner, int pageCount)
    {
        var dialog = new ExtractSelectionDialog(pageCount) { Owner = owner };
        if (dialog.ShowDialog() != true || !dialog.TryExpand(out IReadOnlyList<int>? pages))
        {
            return (null, false);
        }
        return (pages, true);
    }

    /// <summary>Expand the range string into ordered, de-duplicated 0-based indices. Accepts single
    /// numbers (<c>"5"</c>) and inclusive ranges (<c>"7-10"</c>) separated by comma/semicolon. Any
    /// non-numeric token, page &lt; 1, page &gt; <see cref="_pageCount"/>, or <c>end &lt; start</c>
    /// fails. Duplicates are dropped (the service forbids repeating a page).</summary>
    private bool TryExpand(out IReadOnlyList<int>? pages)
    {
        pages = null;
        if (string.IsNullOrWhiteSpace(_selectionText))
        {
            return false;
        }

        var ordered = new List<int>();
        var seen = new HashSet<int>();
        foreach (string raw in _selectionText.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!TryParseSegment(raw.Trim(), out int start, out int end))
            {
                return false;
            }
            for (int p = start; p <= end; p++)
            {
                if (seen.Add(p))
                {
                    ordered.Add(p - 1); // 1-based → 0-based
                }
            }
        }

        if (ordered.Count == 0)
        {
            return false;
        }
        pages = ordered;
        return true;
    }

    /// <summary>Parse one segment into an inclusive 1-based [start, end], validating bounds against
    /// the document. Single number yields start == end.</summary>
    private bool TryParseSegment(string token, out int start, out int end)
    {
        start = 0;
        end = 0;
        if (token.Length == 0)
        {
            return false;
        }

        int dash = token.IndexOf('-', StringComparison.Ordinal);
        if (dash < 0)
        {
            return TryParsePage(token, out start) && (end = start) == start;
        }

        return TryParsePage(token[..dash].Trim(), out start)
            && TryParsePage(token[(dash + 1)..].Trim(), out end)
            && end >= start;
    }

    private bool TryParsePage(string token, out int value) =>
        int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
        && value >= 1
        && value <= _pageCount;

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
