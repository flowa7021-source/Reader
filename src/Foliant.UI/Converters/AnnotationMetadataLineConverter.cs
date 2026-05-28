using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Windows.Data;
using Foliant.Domain;

namespace Foliant.UI.Converters;

/// <summary>
/// Строит однострочную метаданные-сводку для аннотации в сайдбаре: <c>«Автор · Тема · 2024-06-20»</c>,
/// пропуская пустые поля. Возвращает пустую строку, если ни одного метаданного нет — это сворачивает
/// строку через <see cref="NullToVisibilityConverter"/> в той же привязке.
/// </summary>
[ValueConversion(typeof(Annotation), typeof(string))]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated via XAML resource declaration; analyzer does not see XAML refs.")]
internal sealed class AnnotationMetadataLineConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Annotation a)
        {
            return string.Empty;
        }

        var parts = new List<string>(3);
        if (!string.IsNullOrEmpty(a.Author))
        {
            parts.Add(a.Author);
        }

        if (!string.IsNullOrEmpty(a.Subject))
        {
            parts.Add(a.Subject);
        }

        if (a.ModifiedAt is { } m)
        {
            parts.Add(m.LocalDateTime.ToString("d", culture));
        }

        return string.Join(" · ", parts);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException($"{nameof(AnnotationMetadataLineConverter)} does not support ConvertBack.");
}
