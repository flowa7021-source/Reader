using System.Text.Json;
using System.Text.Json.Serialization;

namespace Foliant.Application.Services;

/// <summary>
/// Экспортирует словарь form-данных как pretty JSON object {fieldName: value}. Самый удобный
/// формат для backup/restore — несёт ровно ту структуру, что в домене, и совместим с любым
/// другим инструментом.
/// </summary>
public sealed class JsonFormDataExporter : IFormDataExporter
{
    public string FormatName => "JSON";

    public string FileExtension => "json";

    public string Export(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return JsonSerializer.Serialize(values, FormDataJsonContext.Default.IReadOnlyDictionaryStringString);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(IReadOnlyDictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class FormDataJsonContext : JsonSerializerContext;
