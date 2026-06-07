using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using Foliant.Domain;
using Foliant.UI.Localization;

namespace Foliant.UI;

/// <summary>
/// Read-only preflight report: a list of findings derived from a <see cref="PdfPreflightReport"/>,
/// each prefixed with a severity glyph (✓ ok · ⚠ warning · • info). Pure presenter — built from the
/// VM's snapshot; no services. Mirrors <see cref="OutputIntentsDialog"/>. The localized message text
/// (and which fields are warnings) lives here, so the domain report stays pure data.
/// </summary>
public partial class PreflightDialog : Window
{
    private const string Ok = "✓ ";       // ✓
    private const string Warn = "⚠ ";     // ⚠
    private const string Info = "• ";     // •

    public PreflightDialog()
    {
        InitializeComponent();
    }

    /// <summary>Open the dialog modally, presenting <paramref name="report"/> as findings (or an
    /// «unavailable» line when the report is null).</summary>
    /// <param name="owner">Owner window for centring.</param>
    /// <param name="report">The preflight report, or null if analysis failed.</param>
    public static void Show(Window? owner, PdfPreflightReport? report)
    {
        var dialog = new PreflightDialog { Owner = owner };
        var loc = LocalizationManager.Instance;

        if (report is null)
        {
            dialog.FindingsList.Items.Add(loc["PreflightUnavailable"]);
        }
        else
        {
            foreach (string line in BuildFindings(report, loc))
            {
                dialog.FindingsList.Items.Add(line);
            }
        }

        dialog.ShowDialog();
    }

    private static IEnumerable<string> BuildFindings(PdfPreflightReport r, LocalizationManager loc)
    {
        CultureInfo c = CultureInfo.CurrentCulture;

        yield return Info + string.Format(c, loc["PreflightPages"], r.PageCount);

        yield return Info + (string.IsNullOrEmpty(r.PdfVersion)
            ? loc["PreflightVersionUnknown"]
            : string.Format(c, loc["PreflightVersion"], r.PdfVersion));

        yield return r.IsEncrypted
            ? Warn + loc["PreflightEncrypted"]
            : Ok + loc["PreflightNotEncrypted"];

        // Fonts.
        if (r.FontCount == 0)
        {
            yield return Info + loc["PreflightFontsNone"];
        }
        else if (r.NonEmbeddedFontCount > 0)
        {
            yield return Warn + string.Format(c, loc["PreflightFontsNotEmbedded"], r.NonEmbeddedFontCount, r.FontCount);
        }
        else
        {
            yield return Ok + string.Format(c, loc["PreflightFontsAllEmbedded"], r.FontCount);
        }

        yield return r.HasJavaScriptOrActions
            ? Warn + loc["PreflightHasJs"]
            : Ok + loc["PreflightNoJs"];

        // Output intents (PDF/X readiness).
        if (r.HasIccOutputIntent)
        {
            yield return Ok + loc["PreflightOutputIntentIcc"];
        }
        else if (r.OutputIntentCount > 0)
        {
            yield return Info + string.Format(c, loc["PreflightOutputIntentNoIcc"], r.OutputIntentCount);
        }
        else
        {
            yield return Info + loc["PreflightNoOutputIntent"];
        }

        yield return Info + string.Format(c, loc["PreflightLinks"], r.LinkCount);

        yield return r.HasExtractableText
            ? Ok + loc["PreflightHasText"]
            : Warn + loc["PreflightNoText"];
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
