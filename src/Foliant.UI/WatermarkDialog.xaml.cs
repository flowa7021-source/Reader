using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Foliant.Domain;

namespace Foliant.UI;

/// <summary>
/// Collects a <see cref="WatermarkSpec"/> from the user. Mirrors <see cref="InputDialog"/>'s
/// self-DataContext + static <see cref="Prompt"/> pattern — small enough not to warrant a
/// separate VM. Returns <c>null</c> on Cancel/Esc; otherwise a validated spec.
/// </summary>
public partial class WatermarkDialog : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _text = "DRAFT";
    private double _fontSize = 48;
    private double _opacity = 0.3;
    private double _angle = 45;
    private double _r = 128;
    private double _g = 128;
    private double _b = 128;
    private string _pageRange = string.Empty;

    public WatermarkDialog()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) => { TextBox.Focus(); TextBox.SelectAll(); };
    }

    public string Text
    {
        get => _text;
        set { _text = value; Notify(); Notify(nameof(IsValid)); }
    }

    public double SpecFontSize { get => _fontSize; set { _fontSize = value; Notify(); } }
    public double SpecOpacity { get => _opacity; set { _opacity = value; Notify(); } }
    public double Angle { get => _angle; set { _angle = value; Notify(); } }
    public double R { get => _r; set { _r = value; Notify(); } }
    public double G { get => _g; set { _g = value; Notify(); } }
    public double B { get => _b; set { _b = value; Notify(); } }

    /// <summary>Диапазон страниц в формате <c>"1-3,5,7-10"</c>. Пустая строка = все страницы.</summary>
    public string PageRangeText
    {
        get => _pageRange;
        set { _pageRange = value; Notify(); Notify(nameof(IsValid)); }
    }

    /// <summary>OK enabled только когда у текста есть хоть один не-whitespace символ И
    /// page-range parsable (пустой = OK). Сервис бросает ArgumentException на пустом тексте /
    /// невалидном range — лучше дисейблить заранее.</summary>
    public bool IsValid => !string.IsNullOrWhiteSpace(_text) && PageRange.TryParse(_pageRange, out _);

    /// <summary>Open the dialog modally. Returns <c>null</c> if the user cancelled, otherwise
    /// a fresh <see cref="WatermarkSpec"/> with the captured values.</summary>
    public static WatermarkSpec? Prompt(Window? owner)
    {
        var dialog = new WatermarkDialog { Owner = owner };
        if (dialog.ShowDialog() != true)
        {
            return null;
        }
        PageRange? range = null;
        if (PageRange.TryParse(dialog.PageRangeText, out PageRange? parsed) && parsed is not null && !parsed.IsAll)
        {
            range = parsed;
        }
        return new WatermarkSpec(
            Text: dialog.Text.Trim(),
            FontSize: dialog.SpecFontSize,
            Opacity: dialog.SpecOpacity,
            AngleDegrees: dialog.Angle,
            R: (byte)Math.Clamp(dialog.R, 0, 255),
            G: (byte)Math.Clamp(dialog.G, 0, 255),
            B: (byte)Math.Clamp(dialog.B, 0, 255),
            Range: range);
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
