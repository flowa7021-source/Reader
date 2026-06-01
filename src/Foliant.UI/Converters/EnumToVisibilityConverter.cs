using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Foliant.UI.Converters;

/// <summary>
/// One-way converter: returns <see cref="Visibility.Visible"/> when the bound value equals the
/// <c>ConverterParameter</c>, otherwise <see cref="Visibility.Collapsed"/>. Type-agnostic
/// (uses <see cref="object.Equals(object?)"/>); used to show/hide tool-specific panels in the
/// annotation palette (e.g. stamp label combo when ActiveTool == Stamp).
/// </summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated via XAML in MainWindow.xaml; analyzer does not see XAML refs.")]
internal sealed class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not null && parameter is not null && value.Equals(parameter)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
