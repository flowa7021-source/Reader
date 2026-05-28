using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Windows.Data;

namespace Foliant.UI.Converters;

/// <summary>
/// Превращает 0-based <c>PageIndex</c> в строку "N." (1-based), пригодную для prefix'а
/// в bookmark/annotation-листах. <c>null</c> или не-int → пустая строка.
/// </summary>
[ValueConversion(typeof(int), typeof(string))]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated via XAML resource declaration; analyzer does not see XAML refs.")]
internal sealed class OneBasedPageConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int i)
        {
            return (i + 1).ToString(culture) + ".";
        }

        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException($"{nameof(OneBasedPageConverter)} does not support ConvertBack.");
}
