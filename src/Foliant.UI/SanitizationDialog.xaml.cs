using System.Text;
using System.Windows;
using Foliant.Domain;
using Foliant.UI.Localization;

namespace Foliant.UI;

/// <summary>
/// Shows what document-level JavaScript / automatic actions a PDF contains and offers to remove them
/// (save a cleaned copy). The dialog is a pure presenter: it returns whether the user chose to remove;
/// the View performs the Save-As and forwards to <c>DocumentTabViewModel.RemoveJavaScriptCommand</c>.
/// </summary>
public partial class SanitizationDialog : Window
{
    public SanitizationDialog()
    {
        InitializeComponent();
    }

    /// <summary>Open the dialog modally for <paramref name="report"/>. Returns <see langword="true"/>
    /// only if the user clicked Remove (enabled when there is something to remove).</summary>
    /// <param name="owner">Owner window for centring.</param>
    /// <param name="report">The scan result to present.</param>
    public static bool Prompt(Window? owner, PdfSanitizationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var dialog = new SanitizationDialog { Owner = owner };
        dialog.SummaryText.Text = BuildSummary(report);
        dialog.RemoveButton.IsEnabled = report.HasAnyJavaScriptOrActions;
        return dialog.ShowDialog() == true;
    }

    private static string BuildSummary(PdfSanitizationReport report)
    {
        var loc = LocalizationManager.Instance;
        if (!report.HasAnyJavaScriptOrActions)
        {
            return loc["SanitizeNothingFound"];
        }

        var sb = new StringBuilder();
        sb.Append(loc["SanitizeFoundHeader"]).Append('\n');
        if (report.HasJavaScriptOpenAction)
        {
            sb.Append('\n').Append(loc["SanitizeOpenActionJs"]);
        }

        if (report.DocumentJavaScriptNames.Count > 0)
        {
            sb.Append('\n').Append(loc["SanitizeDocScriptsLabel"]).Append(' ')
              .Append(string.Join(", ", report.DocumentJavaScriptNames));
        }

        if (report.HasDocumentAdditionalActions)
        {
            sb.Append('\n').Append(loc["SanitizeDocActions"]);
        }

        sb.Append("\n\n").Append(loc["SanitizeRemoveHint"]);
        return sb.ToString();
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCloseClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
