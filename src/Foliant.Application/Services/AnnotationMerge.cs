using System.Globalization;
using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Слияние импортированных аннотаций (Q-F17/Q-F19) с уже существующими без дублей. Импорт
/// генерирует новые <see cref="Annotation.Id"/>, поэтому совпадение определяется по содержимому,
/// а не по Id. В ключ НЕ входит <see cref="Annotation.CreatedAt"/>: повторный импорт одного файла
/// (или XFDF без <c>creationdate</c>, где импортёр подставляет текущее время) обязан быть
/// идемпотентным. Чистая функция — без I/O и состояния.
/// </summary>
public static class AnnotationMerge
{
    /// <summary>Подмножество <paramref name="incoming"/>, которого ещё нет в
    /// <paramref name="existing"/> (по содержимому). Дубли внутри <paramref name="incoming"/>
    /// тоже схлопываются — каждая уникальная аннотация попадает в результат не более раза.
    /// Порядок исходного <paramref name="incoming"/> сохраняется.</summary>
    public static IReadOnlyList<Annotation> NewAnnotations(
        IReadOnlyList<Annotation> existing,
        IReadOnlyList<Annotation> incoming)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(incoming);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in existing)
        {
            seen.Add(Signature(a));
        }

        var result = new List<Annotation>();
        foreach (var a in incoming)
        {
            if (seen.Add(Signature(a)))
            {
                result.Add(a);
            }
        }

        return result;
    }

    private static string Signature(Annotation a) => string.Join(
        '|',
        a.Kind.ToString(),
        a.PageIndex.ToString(CultureInfo.InvariantCulture),
        a.ColorHex.Trim().ToUpperInvariant(),
        a.Bounds is { } b ? $"{F(b.X)},{F(b.Y)},{F(b.Width)},{F(b.Height)}" : "-",
        a.Text ?? "-",
        a.InkPoints is { } pts ? string.Join(';', pts.Select(p => $"{F(p.X)},{F(p.Y)}")) : "-");

    private static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
