using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;

namespace Foliant.UI;

/// <summary>
/// Collects a single positive integer — how many pages each split file should contain. Mirrors
/// <see cref="CropDialog"/>'s self-DataContext + static <see cref="Prompt"/> shape. The property is
/// named <c>SplitChunkSize</c> (not <c>Width</c>/etc.) to avoid shadowing a <see cref="Window"/>
/// member. Returns <c>(0, false)</c> on Cancel/Esc; otherwise the validated chunk size.
/// </summary>
public partial class SplitEveryDialog : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _chunkSize = "10";

    public SplitEveryDialog()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) => { ChunkBox.Focus(); ChunkBox.SelectAll(); };
    }

    /// <summary>Pages-per-file as a raw string so partial/invalid input is editable; parsed on OK.</summary>
    public string SplitChunkSize { get => _chunkSize; set { _chunkSize = value; Notify(); Notify(nameof(IsValid)); } }

    /// <summary>OK enabled only when the text parses as an integer ≥ 1 (matches the service contract
    /// that <c>pagesPerChunk &gt; 0</c>).</summary>
    public bool IsValid => TryParseChunk(out int n) && n >= 1;

    /// <summary>Open the dialog modally. Returns <c>(0, false)</c> on cancel/invalid input, otherwise
    /// the validated pages-per-file count for <c>DocumentTabViewModel.SplitEveryCommand</c>.</summary>
    public static (int pagesPerChunk, bool ok) Prompt(Window? owner)
    {
        var dialog = new SplitEveryDialog { Owner = owner };
        if (dialog.ShowDialog() != true || !dialog.TryParseChunk(out int n) || n < 1)
        {
            return (0, false);
        }
        return (n, true);
    }

    private bool TryParseChunk(out int value) =>
        int.TryParse(_chunkSize, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
