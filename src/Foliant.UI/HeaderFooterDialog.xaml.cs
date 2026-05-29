using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Foliant.Domain;

namespace Foliant.UI;

/// <summary>
/// Collects a <see cref="HeaderFooterSpec"/>. At least one of header/footer must be
/// non-empty (HeaderFooterSpec accepts nulls but a spec with both empty would be a no-op
/// from the user's perspective). Returns <c>null</c> on Cancel/Esc.
/// </summary>
public partial class HeaderFooterDialog : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _headerText = string.Empty;
    private string _footerText = "{page} / {total}";
    private double _fontSize = 10;
    private double _r = 64;
    private double _g = 64;
    private double _b = 64;

    public HeaderFooterDialog()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) => { HeaderBox.Focus(); HeaderBox.SelectAll(); };
    }

    public string HeaderText
    {
        get => _headerText;
        set { _headerText = value; Notify(); Notify(nameof(IsValid)); }
    }

    public string FooterText
    {
        get => _footerText;
        set { _footerText = value; Notify(); Notify(nameof(IsValid)); }
    }

    public double SpecFontSize { get => _fontSize; set { _fontSize = value; Notify(); } }
    public double R { get => _r; set { _r = value; Notify(); } }
    public double G { get => _g; set { _g = value; Notify(); } }
    public double B { get => _b; set { _b = value; Notify(); } }

    /// <summary>OK enabled only when at least one band has text — avoids the no-op spec.</summary>
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(_headerText) || !string.IsNullOrWhiteSpace(_footerText);

    /// <summary>Open the dialog modally. Returns <c>null</c> on cancel, otherwise a spec where each
    /// band is either trimmed-non-empty or <c>null</c> (so the service skips it cleanly).</summary>
    public static HeaderFooterSpec? Prompt(Window? owner)
    {
        var dialog = new HeaderFooterDialog { Owner = owner };
        if (dialog.ShowDialog() != true)
        {
            return null;
        }
        return new HeaderFooterSpec(
            HeaderText: string.IsNullOrWhiteSpace(dialog.HeaderText) ? null : dialog.HeaderText.Trim(),
            FooterText: string.IsNullOrWhiteSpace(dialog.FooterText) ? null : dialog.FooterText.Trim(),
            FontSize: dialog.SpecFontSize,
            R: (byte)Math.Clamp(dialog.R, 0, 255),
            G: (byte)Math.Clamp(dialog.G, 0, 255),
            B: (byte)Math.Clamp(dialog.B, 0, 255));
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
