using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using Foliant.Domain;
using Foliant.ViewModels;

namespace Foliant.UI;

/// <summary>
/// Edits custom PDF <c>/Info</c> properties («Custom Properties»): the non-standard key/value entries
/// beyond the six classic fields handled by <see cref="DocumentPropertiesDialog"/>. Pre-filled from
/// the document's current custom entries; on Save-As it builds a <see cref="SaveCustomPropertiesRequest"/>
/// for <c>DocumentTabViewModel.SaveCustomPropertiesCommand</c>.
///
/// <para>Mirrors <see cref="PageLabelsDialog"/>'s self-DataContext + static <c>Prompt</c> + editable
/// row-list shape. Property names avoid <see cref="Window"/> members to dodge CS0108 — the add-form
/// name field is exposed as <c>NameText</c> (not <c>Name</c>, a <c>FrameworkElement</c> member).</para>
/// </summary>
public partial class CustomPropertiesDialog : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _nameText = string.Empty;
    private string _valueText = string.Empty;

    public CustomPropertiesDialog()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) => NameBox.Focus();
    }

    /// <summary>The custom properties currently in the editor, sorted by name. Bound by the list template.</summary>
    public ObservableCollection<CustomPropertyRow> Rows { get; } = [];

    /// <summary>Name (key) of the property being added; blank is ignored on Add. Trimmed when committed.</summary>
    public string NameText { get => _nameText; set { _nameText = value; Notify(); } }

    /// <summary>Value of the property being added; stored verbatim (may be empty).</summary>
    public string ValueText { get => _valueText; set { _valueText = value; Notify(); } }

    /// <summary>Open the dialog modally, pre-filled from <paramref name="current"/>. Returns
    /// <c>(null, false)</c> on cancel, otherwise a <see cref="SaveCustomPropertiesRequest"/> with the
    /// edited entries targeting <paramref name="defaultTargetPath"/>.</summary>
    /// <param name="owner">Owner window for centring.</param>
    /// <param name="current">Existing custom entries to pre-fill (read-only).</param>
    /// <param name="defaultTargetPath">Save-As target chosen by the caller.</param>
    public static (SaveCustomPropertiesRequest? request, bool ok) Prompt(
        Window? owner, IReadOnlyList<PdfCustomProperty> current, string defaultTargetPath)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(defaultTargetPath);

        var dialog = new CustomPropertiesDialog { Owner = owner };
        foreach (var property in current.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            dialog.Rows.Add(CustomPropertyRow.From(property));
        }

        if (dialog.ShowDialog() != true)
        {
            return (null, false);
        }

        var properties = dialog.Rows
            .Select(row => row.Property)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return (new SaveCustomPropertiesRequest(properties, defaultTargetPath), true);
    }

    private void OnAddRowClick(object sender, RoutedEventArgs e)
    {
        string name = NameText.Trim();
        if (name.Length == 0)
        {
            return;
        }

        var property = new PdfCustomProperty(name, ValueText);

        // One entry per name (case-insensitive): adding an existing key replaces its value.
        for (int i = Rows.Count - 1; i >= 0; i--)
        {
            if (string.Equals(Rows[i].Property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                Rows.RemoveAt(i);
            }
        }

        InsertSorted(CustomPropertyRow.From(property));
        NameText = string.Empty;
        ValueText = string.Empty;
    }

    private void InsertSorted(CustomPropertyRow row)
    {
        int i = 0;
        while (i < Rows.Count
            && string.Compare(Rows[i].Property.Name, row.Property.Name, StringComparison.OrdinalIgnoreCase) < 0)
        {
            i++;
        }

        Rows.Insert(i, row);
    }

    private void OnRemoveRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CustomPropertyRow row })
        {
            Rows.Remove(row);
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One editor row: an immutable <see cref="PdfCustomProperty"/> plus a human-readable
/// <see cref="Display"/> string («Name = Value») shown in the list.</summary>
public sealed class CustomPropertyRow
{
    private CustomPropertyRow(PdfCustomProperty property, string display)
    {
        Property = property;
        Display = display;
    }

    /// <summary>The custom property this row represents.</summary>
    public PdfCustomProperty Property { get; }

    /// <summary>Human-readable «Name = Value» description shown in the list.</summary>
    public string Display { get; }

    /// <summary>Build a row (with its display string) from a custom property.</summary>
    /// <param name="property">The property to describe.</param>
    public static CustomPropertyRow From(PdfCustomProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        string display = string.Create(CultureInfo.CurrentCulture, $"{property.Name} = {property.Value}");
        return new CustomPropertyRow(property, display);
    }
}
