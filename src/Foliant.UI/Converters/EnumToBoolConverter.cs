using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Windows.Data;

namespace Foliant.UI.Converters;

/// <summary>
/// Two-way converter between an enum value and a <see cref="bool"/>, keyed by the
/// <c>ConverterParameter</c> (the same enum value). Returns <see langword="true"/> when the bound
/// value equals the parameter — used to drive <c>ToggleButton.IsChecked</c> in the annotation-tool
/// palette (radio-group semantics). Type-agnostic: works for any enum (e.g. <c>AnnotationTool</c>),
/// comparison is by <see cref="object.Equals(object?)"/>.
///
/// On <see cref="ConvertBack"/>, a checked button yields the parameter value; unchecking yields
/// <see cref="Binding.DoNothing"/> so the active selection is cleared only via its own command,
/// not by a sibling toggling off.
/// </summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated via XAML <converters:EnumToBoolConverter/> in MainWindow.xaml; analyzer does not see XAML refs.")]
internal sealed class EnumToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not null && parameter is not null && value.Equals(parameter);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true && parameter is not null ? parameter : Binding.DoNothing;
    }
}
