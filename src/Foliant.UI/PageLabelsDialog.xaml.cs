using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using Foliant.Domain;
using Foliant.ViewModels;

namespace Foliant.UI;

/// <summary>
/// Edits PDF page labels («Number Pages»): a list of numbering ranges, each starting at a 1-based
/// page with a style (arabic / roman / letters / none), optional prefix and start number. Pre-filled
/// from the document's current ranges; on Save-As it builds a <see cref="SavePageLabelsRequest"/> for
/// <c>DocumentTabViewModel.SavePageLabelsCommand</c>.
///
/// Mirrors <see cref="DocumentPropertiesDialog"/>'s self-DataContext + static <c>Prompt</c> shape.
/// Property names avoid <see cref="Window"/> members to dodge CS0108 (e.g. <c>StartPageText</c>).
/// </summary>
public partial class PageLabelsDialog : Window, INotifyPropertyChanged
{
    // Combo order ↔ enum. Numeric styles first (the common case), «prefix only» last.
    private static readonly PdfPageLabelStyle[] StyleByIndex =
    [
        PdfPageLabelStyle.Arabic,
        PdfPageLabelStyle.UpperRoman,
        PdfPageLabelStyle.LowerRoman,
        PdfPageLabelStyle.UpperLetters,
        PdfPageLabelStyle.LowerLetters,
        PdfPageLabelStyle.None,
    ];

    public event PropertyChangedEventHandler? PropertyChanged;

    private int _pageCount = 1;
    private string _startPageText = "1";
    private int _styleIndex;
    private string _prefixText = string.Empty;
    private string _startNumberText = "1";

    public PageLabelsDialog()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) => StartPageBox.Focus();
    }

    /// <summary>The ranges currently in the editor, sorted by start page. Bound by the list template.</summary>
    public ObservableCollection<PageLabelRow> Rows { get; } = [];

    /// <summary>1-based start page of the range being added (parsed leniently; invalid → ignored on Add).</summary>
    public string StartPageText { get => _startPageText; set { _startPageText = value; Notify(); } }

    /// <summary>Selected style index into <see cref="StyleByIndex"/> (combo order).</summary>
    public int StyleIndex { get => _styleIndex; set { _styleIndex = value; Notify(); } }

    /// <summary>Optional label prefix of the range being added (e.g. «A-»).</summary>
    public string PrefixText { get => _prefixText; set { _prefixText = value; Notify(); } }

    /// <summary>Start number of the range being added (≥ 1; ignored for the «none» style).</summary>
    public string StartNumberText { get => _startNumberText; set { _startNumberText = value; Notify(); } }

    /// <summary>Open the dialog modally, pre-filled from <paramref name="current"/>. Returns
    /// <c>(null, false)</c> on cancel, otherwise a <see cref="SavePageLabelsRequest"/> with the edited
    /// ranges targeting <paramref name="defaultTargetPath"/>.</summary>
    /// <param name="owner">Owner window for centring.</param>
    /// <param name="current">Existing ranges to pre-fill (read-only).</param>
    /// <param name="pageCount">Document page count, used to bound the start-page input.</param>
    /// <param name="defaultTargetPath">Save-As target chosen by the caller.</param>
    public static (SavePageLabelsRequest? request, bool ok) Prompt(
        Window? owner, IReadOnlyList<PdfPageLabelRange> current, int pageCount, string defaultTargetPath)
    {
        ArgumentNullException.ThrowIfNull(current);

        var dialog = new PageLabelsDialog { Owner = owner };
        dialog._pageCount = Math.Max(1, pageCount);
        foreach (var range in current.OrderBy(r => r.StartPageIndex))
        {
            dialog.Rows.Add(PageLabelRow.From(range));
        }

        if (dialog.ShowDialog() != true)
        {
            return (null, false);
        }

        var ranges = dialog.Rows
            .Select(row => row.Range)
            .OrderBy(r => r.StartPageIndex)
            .ToList();
        return (new SavePageLabelsRequest(ranges, defaultTargetPath), true);
    }

    private void OnAddRowClick(object sender, RoutedEventArgs e)
    {
        if (!TryParseInt(StartPageText, out int startPage1) || startPage1 < 1 || startPage1 > _pageCount)
        {
            return;
        }

        PdfPageLabelStyle style = StyleByIndex[Math.Clamp(StyleIndex, 0, StyleByIndex.Length - 1)];
        int start = TryParseInt(StartNumberText, out int parsed) && parsed >= 1 ? parsed : 1;
        var range = PdfPageLabelRange.Create(startPage1 - 1, style, PrefixText, start);

        // One range per start page: adding the same start replaces the previous entry.
        for (int i = Rows.Count - 1; i >= 0; i--)
        {
            if (Rows[i].Range.StartPageIndex == range.StartPageIndex)
            {
                Rows.RemoveAt(i);
            }
        }

        InsertSorted(PageLabelRow.From(range));
    }

    private void InsertSorted(PageLabelRow row)
    {
        int i = 0;
        while (i < Rows.Count && Rows[i].Range.StartPageIndex < row.Range.StartPageIndex)
        {
            i++;
        }

        Rows.Insert(i, row);
    }

    private void OnRemoveRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PageLabelRow row })
        {
            Rows.Remove(row);
        }
    }

    private static bool TryParseInt(string text, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value)
        || int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private void OnSaveClick(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One editor row: an immutable <see cref="PdfPageLabelRange"/> plus a human-readable
/// <see cref="Display"/> sample built from the domain formatter (e.g. «p.1+ : i, ii, iii…»).</summary>
public sealed class PageLabelRow
{
    private PageLabelRow(PdfPageLabelRange range, string display)
    {
        Range = range;
        Display = display;
    }

    /// <summary>The range this row represents.</summary>
    public PdfPageLabelRange Range { get; }

    /// <summary>Human-readable description shown in the list.</summary>
    public string Display { get; }

    /// <summary>Build a row (with its display string) from a range.</summary>
    /// <param name="range">The range to describe.</param>
    public static PageLabelRow From(PdfPageLabelRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        return new PageLabelRow(range, Describe(range));
    }

    private static string Describe(PdfPageLabelRange range)
    {
        string sample;
        if (range.Style == PdfPageLabelStyle.None)
        {
            sample = string.IsNullOrEmpty(range.Prefix) ? "—" : range.Prefix;
        }
        else
        {
            IReadOnlyList<PdfPageLabelRange> one = [range];
            string a = PdfPageLabelFormatter.Format(one, range.StartPageIndex);
            string b = PdfPageLabelFormatter.Format(one, range.StartPageIndex + 1);
            string c = PdfPageLabelFormatter.Format(one, range.StartPageIndex + 2);
            sample = string.Create(CultureInfo.CurrentCulture, $"{a}, {b}, {c}…");
        }

        return string.Create(CultureInfo.CurrentCulture, $"p.{range.StartPageIndex + 1}+ : {sample}");
    }
}
