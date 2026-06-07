using System.Globalization;
using System.Text;
using System.Windows;
using Foliant.Domain;
using Foliant.UI.Localization;

namespace Foliant.UI;

/// <summary>
/// Read-only list of the document's PDF <c>/OutputIntents</c> (print-production / PDF-X readiness):
/// one line per intent showing the subtype, output condition and whether an ICC profile is embedded.
/// Pure presenter — built from the VM's snapshot; no services. Mirrors <see cref="LinksDialog"/>.
/// </summary>
public partial class OutputIntentsDialog : Window
{
    public OutputIntentsDialog()
    {
        InitializeComponent();
    }

    /// <summary>Open the dialog modally, listing <paramref name="intents"/> (or an «empty» line).</summary>
    /// <param name="owner">Owner window for centring.</param>
    /// <param name="intents">The output intents to display.</param>
    public static void Show(Window? owner, IReadOnlyList<PdfOutputIntent> intents)
    {
        ArgumentNullException.ThrowIfNull(intents);

        var dialog = new OutputIntentsDialog { Owner = owner };
        var loc = LocalizationManager.Instance;
        if (intents.Count == 0)
        {
            dialog.IntentsList.Items.Add(loc["OutputIntentsEmpty"]);
        }
        else
        {
            string iccYes = loc["OutputIntentsIccEmbedded"];
            string iccNo = loc["OutputIntentsIccMissing"];
            foreach (var intent in intents)
            {
                dialog.IntentsList.Items.Add(Describe(intent, iccYes, iccNo));
            }
        }

        dialog.ShowDialog();
    }

    private static string Describe(PdfOutputIntent intent, string iccYes, string iccNo)
    {
        var sb = new StringBuilder(intent.Subtype);

        // Prefer the human-readable condition; fall back to the identifier.
        string? condition = !string.IsNullOrWhiteSpace(intent.OutputCondition)
            ? intent.OutputCondition
            : intent.OutputConditionIdentifier;
        if (!string.IsNullOrWhiteSpace(condition))
        {
            sb.Append("  —  ").Append(condition);
        }

        sb.Append("  [").Append(intent.HasIccProfile ? iccYes : iccNo).Append(']');
        return string.Create(CultureInfo.CurrentCulture, $"{sb}");
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
