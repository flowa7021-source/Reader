using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;

namespace Foliant.UI;

/// <summary>
/// Asks where to insert pages: a 1-based «insert after page» number, where 0 means «before the first
/// page». Returns the 0-based index expected by <c>IPdfInsertPagesService</c> (entered value − 1, so
/// 0 → −1). Mirrors <see cref="DocumentPropertiesDialog"/>'s self-DataContext + static <c>Prompt</c>.
/// </summary>
public partial class InsertPagesDialog : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _positionText;

    public InsertPagesDialog()
    {
        InitializeComponent();
        DataContext = this;
        _positionText = string.Empty;
        Loaded += (_, _) => { PositionBox.SelectAll(); PositionBox.Focus(); };
    }

    /// <summary>1-based «insert after page» value being edited (0 = before the first page).</summary>
    public string PositionText { get => _positionText; set { _positionText = value; Notify(); } }

    /// <summary>Open the dialog modally. Returns <c>(index, false)</c> on cancel, otherwise the
    /// 0-based insert index (−1 = before the first page) for <c>IPdfInsertPagesService.InsertAsync</c>.
    /// Defaults the field to the end of the document.</summary>
    /// <param name="owner">Owner window for centring.</param>
    /// <param name="pageCount">Source document page count, used to bound the input.</param>
    public static (int insertAfterPageIndex, bool ok) Prompt(Window? owner, int pageCount)
    {
        int pages = Math.Max(1, pageCount);
        var dialog = new InsertPagesDialog { Owner = owner };
        dialog.PositionText = pages.ToString(CultureInfo.CurrentCulture); // default: append at the end

        if (dialog.ShowDialog() != true)
        {
            return (0, false);
        }

        // Entered value p ∈ [0, pageCount]; 0 → before first page (−1); p → after page p (index p−1).
        int p = ParsePosition(dialog.PositionText, pages);
        return (p - 1, true);
    }

    private static int ParsePosition(string text, int pageCount)
    {
        if (!TryParseInt(text, out int p))
        {
            return pageCount; // unparsable → append at end
        }

        return Math.Clamp(p, 0, pageCount);
    }

    private static bool TryParseInt(string text, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value)
        || int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
