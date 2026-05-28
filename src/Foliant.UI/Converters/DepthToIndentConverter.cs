using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Foliant.UI.Converters;

/// <summary>
/// Маппит <c>Bookmark.Depth</c> (или любое int) на <see cref="Thickness"/> с левым отступом
/// для рендера иерархических закладок sidebar'ом. По умолчанию 12 px на уровень — достаточно
/// различимо в плотном списке. Отрицательные значения трактуются как 0; <c>null</c> → пустой
/// <see cref="Thickness"/>.
/// </summary>
[ValueConversion(typeof(int), typeof(Thickness))]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated via XAML resource declaration; analyzer does not see XAML refs.")]
internal sealed class DepthToIndentConverter : IValueConverter
{
    private const double PixelsPerLevel = 12.0;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int depth = value switch
        {
            int i => i,
            null => 0,
            _ => 0,
        };

        return new Thickness(Math.Max(0, depth) * PixelsPerLevel, 0, 0, 0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException($"{nameof(DepthToIndentConverter)} does not support ConvertBack.");
}
