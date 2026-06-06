using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace Foliant.UI;

/// <summary>
/// Views and edits the document's XMP metadata packet (raw XML). Pre-filled from the current packet;
/// on Save-As it returns the edited text for <c>DocumentTabViewModel.SaveXmpCommand</c>. Mirrors
/// <see cref="DocumentPropertiesDialog"/>'s self-DataContext + static <c>Prompt</c> shape.
/// </summary>
public partial class XmpMetadataDialog : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _xmpText = string.Empty;

    public XmpMetadataDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    /// <summary>The XMP packet text being edited.</summary>
    public string XmpText { get => _xmpText; set { _xmpText = value; Notify(); } }

    /// <summary>Open the dialog modally, pre-filled from <paramref name="currentXmp"/> (a starter
    /// template when the document has none). Returns <c>(null, false)</c> on cancel, otherwise the
    /// edited packet.</summary>
    /// <param name="owner">Owner window for centring.</param>
    /// <param name="currentXmp">Existing XMP packet, or null when the document has none.</param>
    public static (string? packet, bool ok) Prompt(Window? owner, string? currentXmp)
    {
        var dialog = new XmpMetadataDialog
        {
            Owner = owner,
            XmpText = string.IsNullOrEmpty(currentXmp) ? StarterTemplate : currentXmp,
        };

        if (dialog.ShowDialog() != true)
        {
            return (null, false);
        }

        return (dialog.XmpText, true);
    }

    private const string StarterTemplate =
        "<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n" +
        "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">\n" +
        "  <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\n" +
        "    <rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\">\n" +
        "      <dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\"></rdf:li></rdf:Alt></dc:title>\n" +
        "    </rdf:Description>\n" +
        "  </rdf:RDF>\n" +
        "</x:xmpmeta>\n" +
        "<?xpacket end=\"w\"?>";

    private void OnSaveClick(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
