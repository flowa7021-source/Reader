using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using Foliant.Domain;
using Foliant.UI.Localization;
using Foliant.ViewModels;

namespace Foliant.UI;

/// <summary>
/// Read-only view of a PDF document's digital signatures (Q-F25). Eager snapshot pulled
/// from <see cref="DocumentTabViewModel.LoadDocumentSignatures"/> on construction; per-row
/// «Validate» button delegates to <see cref="DocumentTabViewModel.ValidateSignatureAsync"/>,
/// which performs PAdES-B validation (CMS signature + ByteRange integrity + certificate chain,
/// Q-F26 partial). The result is a colour-coded banner: green when valid + trusted + untouched,
/// orange when cryptographically valid but with trust/integrity caveats, red when invalid.
/// T-level (TSA) / revocation remain Phase 2 follow-ups.
/// </summary>
public partial class SignaturesDialog : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly DocumentTabViewModel _tab;
    private SignatureRow? _selectedSignature;
    private string _validationMessage = string.Empty;
    private Brush _validationBrush = Brushes.OrangeRed;

    public SignaturesDialog(DocumentTabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        _tab = tab;
        var sigs = tab.LoadDocumentSignatures();
        Signatures = [.. sigs.Select(s => new SignatureRow(s))];
        InitializeComponent();
        DataContext = this;
    }

    public ObservableCollection<SignatureRow> Signatures { get; }

    public SignatureRow? SelectedSignature
    {
        get => _selectedSignature;
        set
        {
            _selectedSignature = value;
            Notify();
            Notify(nameof(HasSelection));
        }
    }

    public bool HasSelection => _selectedSignature is not null;

    public string ValidationMessage
    {
        get => _validationMessage;
        private set { _validationMessage = value; Notify(); }
    }

    /// <summary>Banner colour for the latest validation outcome: green = valid + trusted +
    /// untouched, orange = valid with caveats, red = invalid or error.</summary>
    public Brush ValidationBrush
    {
        get => _validationBrush;
        private set { _validationBrush = value; Notify(); }
    }

    /// <summary>Top-of-dialog label: either "N signatures" or «no signatures» hint.</summary>
    public string HeaderText
    {
        get
        {
            var loc = LocalizationManager.Instance;
            if (Signatures.Count == 0)
            {
                return loc["SignaturesEmptyHint"];
            }

            // Substitute {0} directly instead of `string.Format` — the format string is
            // loaded from resources, so static analyzers (CodeQL cs/format-argument-count)
            // can't verify the placeholder/arg count and flag it.
            return loc["SignaturesCountFormat"].Replace(
                "{0}",
                Signatures.Count.ToString(CultureInfo.CurrentCulture),
                StringComparison.Ordinal);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Validation failure must not crash the dialog.")]
    private async void OnValidateClick(object sender, RoutedEventArgs e)
    {
        if (_selectedSignature is null)
        {
            return;
        }

        try
        {
            SignatureValidationResult? result = await _tab.ValidateSignatureAsync(
                _selectedSignature.Source, CancellationToken.None).ConfigureAwait(true);
            ApplyResult(result);
        }
        catch (Exception ex)
        {
            ValidationMessage = ex.Message;
            ValidationBrush = Brushes.OrangeRed;
        }
    }

    /// <summary>Compose the colour-coded banner from a validation outcome. A null result (no
    /// parsable signature) and a crypto failure both render red; a valid signature renders green
    /// only when the certificate is trusted and the document is untouched, otherwise orange.</summary>
    private void ApplyResult(SignatureValidationResult? result)
    {
        var loc = LocalizationManager.Instance;
        if (result is null || !result.IsValid)
        {
            string reason = result?.FailureReason is { Length: > 0 } r ? " " + r : string.Empty;
            ValidationMessage = loc["SignatureInvalidResult"] + reason;
            ValidationBrush = Brushes.OrangeRed;
            return;
        }

        string trust = result.CertificateTrusted ? loc["SignatureTrustedYes"] : loc["SignatureTrustedNo"];
        string integrity = result.DocumentUntouchedSinceSigning ? loc["SignatureUntouchedYes"] : loc["SignatureModified"];
        ValidationMessage = $"{loc["SignatureValidResult"]} — {trust}, {integrity}.";
        ValidationBrush = result.CertificateTrusted && result.DocumentUntouchedSinceSigning
            ? Brushes.SeaGreen
            : Brushes.DarkOrange;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One <see cref="DocumentSignature"/> with view-friendly formatted columns.
/// Decouples the dialog from <see cref="DateTimeOffset"/>/enum formatting in XAML.</summary>
public sealed class SignatureRow
{
    public SignatureRow(DocumentSignature source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
    }

    public DocumentSignature Source { get; }
    public string SignerName => Source.SignerName;
    public string SignedAtDisplay => Source.SignedAt.UtcDateTime.ToString(
        "yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
    public string KindDisplay => Source.Kind.ToString();
    public string Reason => Source.Reason ?? string.Empty;
    public string Location => Source.Location ?? string.Empty;
}
