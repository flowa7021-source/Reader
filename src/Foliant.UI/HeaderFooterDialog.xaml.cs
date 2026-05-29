using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Foliant.Domain;

namespace Foliant.UI;

/// <summary>
/// Collects a <see cref="HeaderFooterSpec"/>. Six textboxes — по одному на каждую позицию
/// (TopLeft/TopCenter/TopRight + BottomLeft/BottomCenter/BottomRight). Хотя бы один должен
/// быть непустым. Возвращает <c>null</c> на Cancel/Esc.
/// </summary>
public partial class HeaderFooterDialog : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _topLeft = string.Empty;
    private string _topCenter = string.Empty;
    private string _topRight = string.Empty;
    private string _bottomLeft = string.Empty;
    private string _bottomCenter = "{page} / {total}";
    private string _bottomRight = string.Empty;
    private double _fontSize = 10;
    private double _r = 64;
    private double _g = 64;
    private double _b = 64;
    private string _pageRange = string.Empty;

    public HeaderFooterDialog()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) => { TopCenterBox.Focus(); TopCenterBox.SelectAll(); };
    }

    public string TopLeftText
    {
        get => _topLeft;
        set { _topLeft = value; Notify(); Notify(nameof(IsValid)); }
    }

    public string TopCenterText
    {
        get => _topCenter;
        set { _topCenter = value; Notify(); Notify(nameof(IsValid)); }
    }

    public string TopRightText
    {
        get => _topRight;
        set { _topRight = value; Notify(); Notify(nameof(IsValid)); }
    }

    public string BottomLeftText
    {
        get => _bottomLeft;
        set { _bottomLeft = value; Notify(); Notify(nameof(IsValid)); }
    }

    public string BottomCenterText
    {
        get => _bottomCenter;
        set { _bottomCenter = value; Notify(); Notify(nameof(IsValid)); }
    }

    public string BottomRightText
    {
        get => _bottomRight;
        set { _bottomRight = value; Notify(); Notify(nameof(IsValid)); }
    }

    public double SpecFontSize { get => _fontSize; set { _fontSize = value; Notify(); } }
    public double R { get => _r; set { _r = value; Notify(); } }
    public double G { get => _g; set { _g = value; Notify(); } }
    public double B { get => _b; set { _b = value; Notify(); } }

    public string PageRangeText
    {
        get => _pageRange;
        set { _pageRange = value; Notify(); Notify(nameof(IsValid)); }
    }

    /// <summary>OK enabled только когда хотя бы один из шести полей не-blank И page-range parses.</summary>
    public bool IsValid =>
        (!string.IsNullOrWhiteSpace(_topLeft) || !string.IsNullOrWhiteSpace(_topCenter) || !string.IsNullOrWhiteSpace(_topRight)
         || !string.IsNullOrWhiteSpace(_bottomLeft) || !string.IsNullOrWhiteSpace(_bottomCenter) || !string.IsNullOrWhiteSpace(_bottomRight))
        && PageRange.TryParse(_pageRange, out _);

    /// <summary>Open dialog modally. Returns <c>null</c> on cancel; иначе spec со списком
    /// непустых bands (trimmed) по позициям, plus parsed page-range.</summary>
    public static HeaderFooterSpec? Prompt(Window? owner)
    {
        var dialog = new HeaderFooterDialog { Owner = owner };
        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        PageRange? range = null;
        if (PageRange.TryParse(dialog.PageRangeText, out PageRange? parsed) && parsed is not null && !parsed.IsAll)
        {
            range = parsed;
        }

        var bands = new List<HeaderFooterBand>(6);
        AddIfPresent(bands, HeaderFooterPosition.TopLeft, dialog.TopLeftText);
        AddIfPresent(bands, HeaderFooterPosition.TopCenter, dialog.TopCenterText);
        AddIfPresent(bands, HeaderFooterPosition.TopRight, dialog.TopRightText);
        AddIfPresent(bands, HeaderFooterPosition.BottomLeft, dialog.BottomLeftText);
        AddIfPresent(bands, HeaderFooterPosition.BottomCenter, dialog.BottomCenterText);
        AddIfPresent(bands, HeaderFooterPosition.BottomRight, dialog.BottomRightText);

        return new HeaderFooterSpec(
            Bands: bands,
            FontSize: dialog.SpecFontSize,
            R: (byte)Math.Clamp(dialog.R, 0, 255),
            G: (byte)Math.Clamp(dialog.G, 0, 255),
            B: (byte)Math.Clamp(dialog.B, 0, 255),
            Range: range);
    }

    private static void AddIfPresent(List<HeaderFooterBand> sink, HeaderFooterPosition pos, string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            sink.Add(new HeaderFooterBand(pos, text.Trim()));
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
