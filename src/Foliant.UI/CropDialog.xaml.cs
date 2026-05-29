using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Foliant.Domain;

namespace Foliant.UI;

/// <summary>
/// Collects a <see cref="CropSpec"/> from the user — four trim fractions (0..50 %) per side.
/// Mirrors <see cref="WatermarkDialog"/>'s self-DataContext + static <see cref="Prompt"/>
/// pattern. Returns <c>null</c> on Cancel/Esc; otherwise a validated spec.
/// </summary>
public partial class CropDialog : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private double _trimLeft;
    private double _trimTop;
    private double _trimRight;
    private double _trimBottom;
    private bool _isPhysical;

    public CropDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    public double TrimLeft { get => _trimLeft; set { _trimLeft = value; Notify(); Notify(nameof(IsValid)); } }
    public double TrimTop { get => _trimTop; set { _trimTop = value; Notify(); Notify(nameof(IsValid)); } }
    public double TrimRight { get => _trimRight; set { _trimRight = value; Notify(); Notify(nameof(IsValid)); } }
    public double TrimBottom { get => _trimBottom; set { _trimBottom = value; Notify(); Notify(nameof(IsValid)); } }

    /// <summary>True → <see cref="CropMode.Physical"/>; false → <see cref="CropMode.Reversible"/>.
    /// Two-way bound to a RadioButton group in XAML.</summary>
    public bool IsPhysical { get => _isPhysical; set { _isPhysical = value; Notify(); Notify(nameof(IsReversible)); } }

    /// <summary>Inverse of <see cref="IsPhysical"/> для второго RadioButton'а — две взаимо-
    /// исключающие опции в одной IsChecked-биндинг-парадигме.</summary>
    public bool IsReversible { get => !_isPhysical; set { _isPhysical = !value; Notify(); Notify(nameof(IsPhysical)); } }

    /// <summary>OK enabled only when the spec has a measurable effect AND the sums stay below
    /// the safe limit (avoid creating a degenerate page). Mirrors <see cref="CropSpec.Validate"/>
    /// + <see cref="CropSpec.HasEffect"/>.</summary>
    public bool IsValid
    {
        get
        {
            var spec = new CropSpec(_trimLeft, _trimTop, _trimRight, _trimBottom);
            if (!spec.HasEffect)
            {
                return false;
            }
            try
            {
                spec.Validate();
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }
    }

    /// <summary>Open the dialog modally. Returns <c>null</c> on cancel, otherwise a validated spec.</summary>
    public static CropSpec? Prompt(Window? owner)
    {
        var dialog = new CropDialog { Owner = owner };
        if (dialog.ShowDialog() != true)
        {
            return null;
        }
        return new CropSpec(
            dialog.TrimLeft, dialog.TrimTop, dialog.TrimRight, dialog.TrimBottom,
            dialog.IsPhysical ? CropMode.Physical : CropMode.Reversible);
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
