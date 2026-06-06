using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using Foliant.Domain;

namespace Foliant.UI;

/// <summary>The action the user chose in <see cref="AttachmentsDialog"/>.</summary>
public enum AttachmentActionKind
{
    /// <summary>No action (dialog closed / cancelled).</summary>
    None,

    /// <summary>Add a new attachment (caller picks the file + Save-As target).</summary>
    Add,

    /// <summary>Extract the selected attachment (caller picks the destination file).</summary>
    Extract,

    /// <summary>Remove the selected attachment (caller picks the Save-As target).</summary>
    Remove,
}

/// <summary>
/// Lists a PDF's embedded-file attachments and lets the user pick one action — Add, Extract or
/// Remove — returning that choice plus the selected attachment. The View then performs the matching
/// file dialogs and forwards to the VM command (the dialog itself touches no services, mirroring the
/// other dialogs). Add/Extract don't require a selection; Extract/Remove do.
/// </summary>
public partial class AttachmentsDialog : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private AttachmentRow? _selectedRow;

    public AttachmentsDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    /// <summary>The attachment rows shown in the list.</summary>
    public ObservableCollection<AttachmentRow> Items { get; } = [];

    /// <summary>The currently selected list row (or null). Bound to the ListBox selection.</summary>
    public AttachmentRow? SelectedRow { get => _selectedRow; set { _selectedRow = value; Notify(); } }

    /// <summary>The currently selected attachment (or null) — derived from <see cref="SelectedRow"/>.</summary>
    public PdfAttachment? Selected => _selectedRow?.Attachment;

    /// <summary>The action the user chose; valid only when <c>ShowDialog</c> returned true.</summary>
    public AttachmentActionKind ChosenAction { get; private set; } = AttachmentActionKind.None;

    /// <summary>Open the dialog modally, listing <paramref name="current"/>. Returns the chosen action
    /// + the selected attachment (null for Add / when nothing was selected) + ok=false on Close.</summary>
    /// <param name="owner">Owner window for centring.</param>
    /// <param name="current">Attachments to list.</param>
    public static (AttachmentActionKind action, PdfAttachment? selected, bool ok) Prompt(
        Window? owner, IReadOnlyList<PdfAttachment> current)
    {
        ArgumentNullException.ThrowIfNull(current);

        var dialog = new AttachmentsDialog { Owner = owner };
        foreach (var a in current)
        {
            dialog.Items.Add(AttachmentRow.From(a));
        }

        dialog.EmptyLabel.Visibility = dialog.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (dialog.ShowDialog() != true)
        {
            return (AttachmentActionKind.None, null, false);
        }

        return (dialog.ChosenAction, dialog.Selected, true);
    }

    private void OnAddClick(object sender, RoutedEventArgs e) => Close(AttachmentActionKind.Add);

    private void OnExtractClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not null)
        {
            Close(AttachmentActionKind.Extract);
        }
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not null)
        {
            Close(AttachmentActionKind.Remove);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Close(AttachmentActionKind action)
    {
        ChosenAction = action;
        DialogResult = true;
    }

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One list row: the attachment plus a human-readable <see cref="Display"/> line
/// («name — 12.3 KB — description»).</summary>
public sealed class AttachmentRow
{
    private AttachmentRow(PdfAttachment attachment, string display)
    {
        Attachment = attachment;
        Display = display;
    }

    /// <summary>The attachment this row represents.</summary>
    public PdfAttachment Attachment { get; }

    /// <summary>Human-readable description shown in the list.</summary>
    public string Display { get; }

    /// <summary>Build a row (with its display string) from an attachment.</summary>
    /// <param name="attachment">The attachment to describe.</param>
    public static AttachmentRow From(PdfAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        string size = FormatSize(attachment.Size);
        string display = string.IsNullOrEmpty(attachment.Description)
            ? string.Create(CultureInfo.CurrentCulture, $"{attachment.Name}  —  {size}")
            : string.Create(CultureInfo.CurrentCulture, $"{attachment.Name}  —  {size}  —  {attachment.Description}");
        return new AttachmentRow(attachment, display);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{bytes} B");
        }

        double kb = bytes / 1024.0;
        if (kb < 1024)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{kb:0.#} KB");
        }

        double mb = kb / 1024.0;
        return string.Create(CultureInfo.CurrentCulture, $"{mb:0.#} MB");
    }
}
