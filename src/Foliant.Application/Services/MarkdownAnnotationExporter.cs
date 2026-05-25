using System.Globalization;
using System.Text;
using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Markdown-экспорт аннотаций: группирует по странице, заголовок второго
/// уровня — «Page N», под каждой страницей — список аннотаций.
/// </summary>
public sealed class MarkdownAnnotationExporter : IAnnotationExporter
{
    public string FormatName => "Markdown";

    public string FileExtension => "md";

    public string Export(IReadOnlyList<Annotation> annotations)
    {
        ArgumentNullException.ThrowIfNull(annotations);
        if (annotations.Count == 0)
        {
            return "# Annotations\n\n_No annotations._\n";
        }

        var sb = new StringBuilder();
        sb.AppendLine("# Annotations");
        sb.AppendLine();

        var byPage = annotations
            .GroupBy(a => a.PageIndex)
            .OrderBy(g => g.Key);

        foreach (var group in byPage)
        {
            sb.Append("## Page ");
            sb.Append((group.Key + 1).ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();
            sb.AppendLine();

            foreach (var ann in group.OrderBy(a => a.CreatedAt))
            {
                AppendAnnotation(sb, ann);
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void AppendAnnotation(StringBuilder sb, Annotation a)
    {
        switch (a.Kind)
        {
            case AnnotationKind.Highlight:
                sb.Append("- **Highlight** (");
                sb.Append(a.ColorHex);
                sb.AppendLine(")");
                break;

            case AnnotationKind.StickyNote:
                sb.Append("- **Note**: ");
                sb.AppendLine(EscapeMarkdown(a.Text ?? string.Empty));
                break;

            case AnnotationKind.Freehand:
                sb.Append("- **Freehand** (");
                int points = a.InkPoints?.Count ?? 0;
                sb.Append(points.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine(" points)");
                break;
        }
    }

    private static string EscapeMarkdown(string raw)
    {
        // минимальный экранировщик: переносы строк → пробел, чтобы не ломать список.
        return raw.Replace("\r\n", " ", StringComparison.Ordinal)
                  .Replace('\n', ' ')
                  .Replace('\r', ' ');
    }
}
