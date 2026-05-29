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
    private string _imagePath = string.Empty;

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

    /// <summary>Опциональный путь к image-watermark'у. Когда задан, текст игнорируется и
    /// IsValid требует только page-range parse + наличие файла. Пустой ↔ режим текста.</summary>
    public string ImagePath
    {
        get => _imagePath;
        set { _imagePath = value; Notify(); Notify(nameof(IsValid)); }
    }

    /// <summary>OK enabled когда:
    /// <list type="bullet">
    /// <item>page-range parsable (пустой = OK);</item>
    /// <item>ЛИБО image-path задан и файл существует — режим image;</item>
    /// <item>ЛИБО text не пустой — режим text.</item>
    /// </list></summary>
    public bool IsValid
    {
        get
        {
            if (!PageRange.TryParse(_pageRange, out _))
            {
                return false;
            }
            if (!string.IsNullOrWhiteSpace(_imagePath))
            {
                return System.IO.File.Exists(_imagePath);
            }
            return !string.IsNullOrWhiteSpace(_text);
        }
    }

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
        string? imagePath = string.IsNullOrWhiteSpace(dialog.ImagePath) ? null : dialog.ImagePath.Trim();
        return new WatermarkSpec(
            Text: imagePath is null ? dialog.Text.Trim() : string.Empty,
            FontSize: dialog.SpecFontSize,
            Opacity: dialog.SpecOpacity,
            AngleDegrees: dialog.Angle,
            R: (byte)Math.Clamp(dialog.R, 0, 255),
            G: (byte)Math.Clamp(dialog.G, 0, 255),
            B: (byte)Math.Clamp(dialog.B, 0, 255),
            Range: range,
            ImagePath: imagePath);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "UI event handler must not propagate exceptions.")]
    private void OnBrowseImageClick(object sender, RoutedEventArgs e)
    {
        var loc = Foliant.UI.Localization.LocalizationManager.Instance;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = loc["WatermarkImagePickTitle"],
            Filter = loc["WatermarkImagePickFilter"],
            CheckFileExists = true,
        };
        try
        {
            if (dialog.ShowDialog(this) == true)
            {
                ImagePath = dialog.FileName;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WatermarkDialog browse-image failed: {ex.Message}");
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
