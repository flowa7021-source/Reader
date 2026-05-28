using System.Text.Json;

namespace Foliant.Application.Services;

/// <summary>
/// Импорт form-данных из JSON — обратная операция к <see cref="JsonFormDataExporter"/>.
/// Принимаем как pretty, так и compact JSON object. Битые JSON-значения (non-string) пропускаются;
/// неваl. JSON top-level бросает <see cref="JsonException"/>.
/// </summary>
public sealed class JsonFormDataImporter : IFormDataImporter
{
    public string FormatName => "JSON";

    public string FileExtension => "json";

    public IReadOnlyDictionary<string, string> Import(string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        using var doc = JsonDocument.Parse(content);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            // Non-string значения (числа, объекты) пропускаем — caller хочет name→string mapping.
            if (prop.Value.ValueKind == JsonValueKind.String && prop.Value.GetString() is { } value)
            {
                result[prop.Name] = value;
            }
        }

        return result;
    }
}
