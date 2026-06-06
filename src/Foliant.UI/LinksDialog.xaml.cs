using System.Globalization;
using System.Windows;
using Foliant.Domain;
using Foliant.UI.Localization;

namespace Foliant.UI;

/// <summary>
/// Read-only list of the document's link annotations (page → URI or page → page). Pure presenter —
/// built from the VM's link snapshot; no services. Mirrors <see cref="FontsDialog"/>.
/// </summary>
public partial class LinksDialog : Window
{
    public LinksDialog()
    {
        InitializeComponent();
    }

    /// <summary>Open the dialog modally, listing <paramref name="links"/> (or an «empty» line).</summary>
    /// <param name="owner">Owner window for centring.</param>
    /// <param name="links">The links to display.</param>
    public static void Show(Window? owner, IReadOnlyList<PdfLinkAnnotation> links)
    {
        ArgumentNullException.ThrowIfNull(links);

        var dialog = new LinksDialog { Owner = owner };
        var loc = LocalizationManager.Instance;
        if (links.Count == 0)
        {
            dialog.LinksList.Items.Add(loc["LinksEmpty"]);
        }
        else
        {
            string toPage = loc["LinksToPage"];
            string other = loc["LinksUnresolved"];
            foreach (var link in links)
            {
                string target = link.Uri is not null
                    ? link.Uri
                    : link.TargetPageIndex is int p
                        ? string.Create(CultureInfo.CurrentCulture, $"{toPage} {p + 1}")
                        : other;
                dialog.LinksList.Items.Add(string.Create(CultureInfo.CurrentCulture, $"p.{link.PageIndex + 1}  →  {target}"));
            }
        }

        dialog.ShowDialog();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
