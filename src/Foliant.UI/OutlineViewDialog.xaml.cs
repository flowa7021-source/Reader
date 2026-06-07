using System.Globalization;
using System.Text;
using System.Windows;
using Foliant.Domain;
using Foliant.UI.Localization;

namespace Foliant.UI;

/// <summary>
/// Read-only viewer of the document's rich PDF outline (Table of Contents): one indented line per
/// node showing title, target page, destination zoom mode, style (bold/italic), colour and
/// open/closed state. Pure presenter — built from the VM's outline snapshot; no services. Mirrors
/// <see cref="LinksDialog"/>.
/// </summary>
public partial class OutlineViewDialog : Window
{
    public OutlineViewDialog()
    {
        InitializeComponent();
    }

    /// <summary>Open the dialog modally, listing <paramref name="entries"/> (or an «empty» line).</summary>
    /// <param name="owner">Owner window for centring.</param>
    /// <param name="entries">The rich outline entries to display (pre-order, depth-tagged).</param>
    public static void Show(Window? owner, IReadOnlyList<DocumentOutlineEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var dialog = new OutlineViewDialog { Owner = owner };
        var loc = LocalizationManager.Instance;
        if (entries.Count == 0)
        {
            dialog.OutlineList.Items.Add(loc["OutlineViewEmpty"]);
        }
        else
        {
            foreach (var entry in entries)
            {
                dialog.OutlineList.Items.Add(Describe(entry, loc));
            }
        }

        dialog.ShowDialog();
    }

    private static string Describe(DocumentOutlineEntry entry, LocalizationManager loc)
    {
        var sb = new StringBuilder();
        sb.Append(' ', entry.Depth * 4);
        sb.Append(entry.Title);

        var attrs = new List<string>(4);
        attrs.Add(entry.PageIndex >= 0
            ? string.Create(CultureInfo.CurrentCulture, $"{loc["OutlineViewPageAbbrev"]}{entry.PageIndex + 1}")
            : loc["OutlineViewNoPage"]);
        attrs.Add(DestLabel(entry.Destination, loc));
        if (entry.IsBold)
        {
            attrs.Add(loc["OutlineViewBold"]);
        }

        if (entry.IsItalic)
        {
            attrs.Add(loc["OutlineViewItalic"]);
        }

        if (entry.Color is not null)
        {
            attrs.Add(loc["OutlineViewColoured"]);
        }

        attrs.Add(entry.IsOpen ? loc["OutlineViewOpen"] : loc["OutlineViewClosed"]);

        sb.Append("  (").Append(string.Join(" · ", attrs)).Append(')');
        return sb.ToString();
    }

    private static string DestLabel(OutlineDestinationMode mode, LocalizationManager loc) => mode switch
    {
        OutlineDestinationMode.FitWidth => loc["OutlineDestFitWidth"],
        OutlineDestinationMode.FitHeight => loc["OutlineDestFitHeight"],
        OutlineDestinationMode.InheritZoom => loc["OutlineDestInheritZoom"],
        _ => loc["OutlineDestFitPage"],
    };

    private void OnCloseClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
