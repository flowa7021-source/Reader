using System.Text.Json;
using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Импорт аннотаций из JSON — обратная операция к <see cref="JsonAnnotationExporter"/> и самый
/// высокоточный формат обмена (несёт все поля, включая <see cref="Annotation.CreatedAt"/>), поэтому
/// удобен для backup/restore. Каждый элемент массива разбирается отдельно: битый/неполный
/// пропускается (best-effort), невалидный JSON верхнего уровня бросает исключение вызывающему.
/// <see cref="Annotation.Id"/> генерируется заново — как и прочие импортёры, считаем импортируемые
/// аннотации новыми сущностями (дедуп по содержимому делает <see cref="AnnotationMerge"/>).
/// </summary>
public sealed class JsonAnnotationImporter : IAnnotationImporter
{
    public string FormatName => "JSON";

    public string FileExtension => "json";

    public IReadOnlyList<Annotation> Import(string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        using var doc = JsonDocument.Parse(content);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<Annotation>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            if (TryParse(element) is { } annotation)
            {
                result.Add(annotation);
            }
        }

        return result;
    }

    private static Annotation? TryParse(JsonElement element)
    {
        Annotation? raw;
        try
        {
            raw = element.Deserialize(AnnotationExportJsonContext.Default.Annotation);
        }
        catch (JsonException)
        {
            return null;
        }

        if (raw is null || !IsValid(raw))
        {
            return null;
        }

        return raw with { Id = Guid.NewGuid() };
    }

    // Те же правила валидности, что у экспортёров: highlight/note/underline/strikethrough/
    // rectangle/ellipse требуют Bounds; freehand/polygon — ≥2 точек; line/arrow — ровно 2.
    private static bool IsValid(Annotation a) => a.Kind switch
    {
        AnnotationKind.Highlight or AnnotationKind.StickyNote
            or AnnotationKind.Underline or AnnotationKind.Strikethrough
            or AnnotationKind.Rectangle or AnnotationKind.Ellipse => a.Bounds is not null,
        AnnotationKind.Freehand or AnnotationKind.Polygon => a.InkPoints is { Count: >= 2 },
        AnnotationKind.Line or AnnotationKind.Arrow => a.InkPoints is { Count: 2 },
        _ => false,
    };
}
