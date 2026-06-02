using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Foliant.Application.Services;
using Foliant.ViewModels;

namespace Foliant.UI;

/// <summary>
/// Collects a find-and-redact query + matching options. MVP: text-based only. Visual mouse-drawn
/// region selection is deliberately deferred (planned for Wave 5+); coordinate-region redaction
/// is still wired through the VM's <c>RedactPagesCommand</c> for batch / programmatic callers.
/// Mirrors <see cref="CropDialog"/>'s self-DataContext + static <see cref="Prompt"/> pattern.
/// </summary>
public partial class RedactionDialog : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _query = string.Empty;
    private bool _caseSensitive;
    private bool _wholeWord;
    private bool _useRegex;
    private bool _foldDiacritics;

    public RedactionDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    public string Query { get => _query; set { _query = value; Notify(); Notify(nameof(IsValid)); } }
    public bool CaseSensitive { get => _caseSensitive; set { _caseSensitive = value; Notify(); } }
    public bool WholeWord { get => _wholeWord; set { _wholeWord = value; Notify(); } }
    public bool UseRegex { get => _useRegex; set { _useRegex = value; Notify(); } }
    public bool FoldDiacritics { get => _foldDiacritics; set { _foldDiacritics = value; Notify(); } }

    /// <summary>OK enabled only when there's a non-blank query to search for. Validating regex
    /// syntax up front would duplicate <see cref="FindAndRedactService"/>'s checks; if the pattern
    /// is malformed the service throws and the MainWindow handler surfaces the message.</summary>
    public bool IsValid => !string.IsNullOrWhiteSpace(_query);

    /// <summary>Open the dialog modally. Returns <c>null</c> on cancel or empty query, otherwise
    /// a <see cref="FindAndRedactRequest"/> ready to be passed to
    /// <c>DocumentTabViewModel.FindAndRedactCommand</c>.</summary>
    public static FindAndRedactRequest? Prompt(Window? owner, string defaultTargetPath)
    {
        var dialog = new RedactionDialog { Owner = owner };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Query))
        {
            return null;
        }
        var options = new FindAndRedactOptions(
            CaseSensitive: dialog.CaseSensitive,
            WholeWord: dialog.WholeWord,
            Regex: dialog.UseRegex,
            FoldDiacritics: dialog.FoldDiacritics);
        return new FindAndRedactRequest(dialog.Query, options, defaultTargetPath);
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
