using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using Foliant.Domain;
using Foliant.ViewModels;

namespace Foliant.UI;

/// <summary>
/// Collects a <see cref="BatesNumberingSpec"/> for legal page numbering. Unlike header/footer the
/// stamp text is not free-form: the user only configures prefix/suffix, the start number, zero-pad
/// width, font size, colour and corner. Numeric fields are validated so the spec contract
/// (<c>Digits &gt;= 1</c>, positive font size, parseable start number) holds before OK is enabled.
/// Mirrors <see cref="HeaderFooterDialog"/>'s self-DataContext + static <see cref="Prompt"/> shape.
/// </summary>
public partial class BatesNumberingDialog : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _prefix = string.Empty;
    private string _suffix = string.Empty;
    private string _startNumber = "1";
    private string _digits = "6";
    private int _positionIndex; // 0=BottomLeft, 1=BottomCenter, 2=BottomRight — matches BatesPosition order
    private double _fontSize = 10;
    private double _r = 64;
    private double _g = 64;
    private double _b = 64;
    private string _pageRange = string.Empty;

    public BatesNumberingDialog()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) => { PrefixBox.Focus(); };
    }

    public string Prefix { get => _prefix; set { _prefix = value; Notify(); } }
    public string Suffix { get => _suffix; set { _suffix = value; Notify(); } }
    public string StartNumberText { get => _startNumber; set { _startNumber = value; Notify(); Notify(nameof(IsValid)); } }
    public string DigitsText { get => _digits; set { _digits = value; Notify(); Notify(nameof(IsValid)); } }
    public int PositionIndex { get => _positionIndex; set { _positionIndex = value; Notify(); } }
    public double StampFontSize { get => _fontSize; set { _fontSize = value; Notify(); } }
    public double R { get => _r; set { _r = value; Notify(); } }
    public double G { get => _g; set { _g = value; Notify(); } }
    public double B { get => _b; set { _b = value; Notify(); } }
    public string PageRangeText { get => _pageRange; set { _pageRange = value; Notify(); Notify(nameof(IsValid)); } }

    /// <summary>OK enabled only when start number parses as an int, digit width is at least 1, and
    /// the optional page-range string parses. Font size comes from a bounded slider so it's always
    /// valid; colour channels likewise.</summary>
    public bool IsValid =>
        TryParseStart(out _)
        && TryParseDigits(out int digits) && digits >= 1
        && PageRange.TryParse(_pageRange, out _);

    /// <summary>Open the dialog modally. Returns <c>(null, false)</c> on cancel/invalid input,
    /// otherwise an <see cref="ApplyBatesRequest"/> targeting <paramref name="defaultTargetPath"/>
    /// ready for <c>DocumentTabViewModel.ApplyBatesCommand</c>.</summary>
    public static (ApplyBatesRequest? request, bool ok) Prompt(Window? owner, string defaultTargetPath)
    {
        var dialog = new BatesNumberingDialog { Owner = owner };
        if (dialog.ShowDialog() != true
            || !dialog.TryParseStart(out int start)
            || !dialog.TryParseDigits(out int digits))
        {
            return (null, false);
        }

        PageRange? range = null;
        if (PageRange.TryParse(dialog.PageRangeText, out PageRange? parsed) && parsed is not null && !parsed.IsAll)
        {
            range = parsed;
        }

        var spec = new BatesNumberingSpec(
            Prefix: dialog.Prefix.Trim(),
            Suffix: dialog.Suffix.Trim(),
            StartNumber: start,
            Digits: digits,
            Position: (BatesPosition)dialog.PositionIndex,
            FontSize: dialog.StampFontSize,
            R: (byte)Math.Clamp(dialog.R, 0, 255),
            G: (byte)Math.Clamp(dialog.G, 0, 255),
            B: (byte)Math.Clamp(dialog.B, 0, 255),
            Range: range);
        return (new ApplyBatesRequest(spec, defaultTargetPath), true);
    }

    private bool TryParseStart(out int value) =>
        int.TryParse(_startNumber, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private bool TryParseDigits(out int value) =>
        int.TryParse(_digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
