using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Foliant.UI.Converters;

/// <summary>
/// Returns <see cref="Visibility.Visible"/> when the value is non-null/non-empty,
/// and <see cref="Visibility.Collapsed"/> when it is null or empty.
/// </summary>
[ValueConversion(typeof(object), typeof(Visibility))]
internal sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string s
            ? (string.IsNullOrEmpty(s) ? Visibility.Collapsed : Visibility.Visible)
            : (value is null ? Visibility.Collapsed : Visibility.Visible);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException($"{nameof(NullToVisibilityConverter)} does not support ConvertBack.");
}
