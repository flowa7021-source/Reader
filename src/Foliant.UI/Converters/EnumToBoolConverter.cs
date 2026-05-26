using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Windows.Data;
using Foliant.Domain;

namespace Foliant.UI.Converters;

/// <summary>
/// Two-way converter between a nullable <see cref="AnnotationKind"/> and a <see cref="bool"/>,
/// keyed by the <c>ConverterParameter</c> (an <see cref="AnnotationKind"/>). Returns
/// <see langword="true"/> when the bound value equals the parameter — used to drive
/// <c>ToggleButton.IsChecked</c> in the annotation-tool palette. On <see cref="ConvertBack"/>,
/// a checked button yields the parameter enum value; unchecking yields <see cref="Binding.DoNothing"/>
/// so the active tool is cleared only via its own command, not by a sibling toggling on.
/// </summary>
[ValueConversion(typeof(AnnotationKind?), typeof(bool), ParameterType = typeof(AnnotationKind))]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated via XAML <converters:EnumToBoolConverter/> in MainWindow.xaml; analyzer does not see XAML refs.")]
internal sealed class EnumToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is AnnotationKind kind && parameter is AnnotationKind target && kind == target;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true && parameter is AnnotationKind target ? target : Binding.DoNothing;
    }
}
