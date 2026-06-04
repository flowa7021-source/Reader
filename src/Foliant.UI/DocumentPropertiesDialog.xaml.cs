using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using Foliant.Domain;
using Foliant.ViewModels;

namespace Foliant.UI;

/// <summary>
/// Edits classic PDF <c>/Info</c> Document Properties (Title/Author/Subject/Keywords/Creator/
/// Producer) and shows read-only Created/Modified dates. Pre-filled from the open document's
/// <see cref="DocumentMetadata"/>; on Save-As it builds a <see cref="PdfMetadataSpec"/> for
/// <c>DocumentTabViewModel.SaveMetadataCommand</c>.
///
/// Empty-field semantics: a blank TextBox maps to <c>null</c> in the spec, i.e. "leave unchanged".
/// This is the safe default — the user opening the dialog and clicking Save without touching a
/// field never accidentally clears metadata that was present (or absent) in the source. Clearing a
/// field is therefore not expressible from this MVP dialog; that's an acceptable trade for not
/// silently wiping values. The doc-title field is exposed as <c>DocTitle</c> (not <c>Title</c>)
/// because <c>Title</c> is a <see cref="Window"/> member — naming it that would shadow it (CS0108).
///
/// Mirrors <see cref="BatesNumberingDialog"/>'s self-DataContext + static <c>Prompt</c> shape.
/// </summary>
public partial class DocumentPropertiesDialog : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _docTitle = string.Empty;
    private string _author = string.Empty;
    private string _subject = string.Empty;
    private string _keywords = string.Empty;
    private string _creator = string.Empty;
    private string _producer = string.Empty;

    public DocumentPropertiesDialog()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) => { TitleBox.Focus(); };
    }

    public string DocTitle { get => _docTitle; set { _docTitle = value; Notify(); } }
    public string Author { get => _author; set { _author = value; Notify(); } }
    public string Subject { get => _subject; set { _subject = value; Notify(); } }
    public string Keywords { get => _keywords; set { _keywords = value; Notify(); } }
    public string Creator { get => _creator; set { _creator = value; Notify(); } }
    public string Producer { get => _producer; set { _producer = value; Notify(); } }

    /// <summary>Created date for the read-only label, formatted in the current UI culture; empty
    /// when the source has no <c>/CreationDate</c>.</summary>
    public string CreatedDisplay { get; private set; } = string.Empty;

    /// <summary>Modified date for the read-only label, formatted in the current UI culture; empty
    /// when the source has no <c>/ModDate</c>.</summary>
    public string ModifiedDisplay { get; private set; } = string.Empty;

    /// <summary>Open the dialog modally, pre-filled from <paramref name="current"/>. Returns
    /// <c>(null, false)</c> on cancel, otherwise a <see cref="SaveMetadataRequest"/> targeting
    /// <paramref name="defaultTargetPath"/> ready for <c>DocumentTabViewModel.SaveMetadataCommand</c>.</summary>
    public static (SaveMetadataRequest? request, bool ok) Prompt(
        Window? owner, DocumentMetadata current, string defaultTargetPath)
    {
        ArgumentNullException.ThrowIfNull(current);

        var dialog = new DocumentPropertiesDialog
        {
            Owner = owner,
            DocTitle = current.Title ?? string.Empty,
            Author = current.Author ?? string.Empty,
            Subject = current.Subject ?? string.Empty,
            CreatedDisplay = Format(current.Created),
            ModifiedDisplay = Format(current.Modified),
        };

        if (dialog.ShowDialog() != true)
        {
            return (null, false);
        }

        var spec = new PdfMetadataSpec(
            Title: ToSpecValue(dialog.DocTitle),
            Author: ToSpecValue(dialog.Author),
            Subject: ToSpecValue(dialog.Subject),
            Keywords: ToSpecValue(dialog.Keywords),
            Creator: ToSpecValue(dialog.Creator),
            Producer: ToSpecValue(dialog.Producer));
        return (new SaveMetadataRequest(spec, defaultTargetPath), true);
    }

    /// <summary>Blank → <c>null</c> ("leave unchanged" per <see cref="PdfMetadataSpec"/>); otherwise
    /// the trimmed value is written as-is.</summary>
    private static string? ToSpecValue(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Format(DateTimeOffset? value) =>
        value?.ToString("g", CultureInfo.CurrentCulture) ?? string.Empty;

    private void OnSaveClick(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
