using System.Text.Json;
using System.Text.Json.Serialization;
using Foliant.Domain;

namespace Foliant.Application.Services;

public sealed class JsonAnnotationExporter : IAnnotationExporter
{
    public string FormatName => "JSON";

    public string FileExtension => "json";

    public string Export(IReadOnlyList<Annotation> annotations)
    {
        ArgumentNullException.ThrowIfNull(annotations);
        return JsonSerializer.Serialize(annotations, AnnotationExportJsonContext.Default.IReadOnlyListAnnotation);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(IReadOnlyList<Annotation>))]
[JsonSerializable(typeof(Annotation))]
[JsonSerializable(typeof(AnnotationRect))]
[JsonSerializable(typeof(AnnotationPoint))]
internal sealed partial class AnnotationExportJsonContext : JsonSerializerContext;
