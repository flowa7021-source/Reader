using System.Globalization;
using System.Windows;
using Foliant.Domain;
using Foliant.UI.Localization;

namespace Foliant.UI;

/// <summary>
/// Read-only list of the document's fonts with embedding status (Acrobat Document Properties → Fonts).
/// Pure presenter — built from the VM's font snapshot; no services. Mirrors the other dialogs' static
/// <c>Show</c> shape.
/// </summary>
public partial class FontsDialog : Window
{
    public FontsDialog()
    {
        InitializeComponent();
    }

    /// <summary>Open the dialog modally, listing <paramref name="fonts"/> (or an «empty» line).</summary>
    /// <param name="owner">Owner window for centring.</param>
    /// <param name="fonts">The fonts to display.</param>
    public static void Show(Window? owner, IReadOnlyList<PdfFontInfo> fonts)
    {
        ArgumentNullException.ThrowIfNull(fonts);

        var dialog = new FontsDialog { Owner = owner };
        var loc = LocalizationManager.Instance;
        if (fonts.Count == 0)
        {
            dialog.FontsList.Items.Add(loc["FontsEmpty"]);
        }
        else
        {
            string embedded = loc["FontsEmbedded"];
            string notEmbedded = loc["FontsNotEmbedded"];
            foreach (var f in fonts)
            {
                string status = f.IsEmbedded ? embedded : notEmbedded;
                dialog.FontsList.Items.Add(string.Create(CultureInfo.CurrentCulture, $"{f.Name}  —  {f.Subtype}  —  {status}"));
            }
        }

        dialog.ShowDialog();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
