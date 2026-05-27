using System.Text.Json;
using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Импорт закладок из JSON — обратная операция к <see cref="JsonBookmarkExporter"/> и лучший
/// формат для backup/restore (несёт все поля). Каждый элемент массива разбирается отдельно:
/// битый/неполный пропускается (best-effort), невалидный JSON верхнего уровня бросает исключение.
/// <see cref="Bookmark.Id"/> генерируется заново; дедуп по содержимому делает
/// <see cref="BookmarkMerge"/>.
/// </summary>
public sealed class JsonBookmarkImporter : IBookmarkImporter
{
    public string FormatName => "JSON";

    public string FileExtension => "json";

    public IReadOnlyList<Bookmark> Import(string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        using var doc = JsonDocument.Parse(content);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<Bookmark>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            if (TryParse(element) is { } bookmark)
            {
                result.Add(bookmark);
            }
        }

        return result;
    }

    private static Bookmark? TryParse(JsonElement element)
    {
        Bookmark? raw;
        try
        {
            raw = element.Deserialize(BookmarkExportJsonContext.Default.Bookmark);
        }
        catch (JsonException)
        {
            return null;
        }

        if (raw is null || raw.PageIndex < 0 || string.IsNullOrWhiteSpace(raw.Label))
        {
            return null;
        }

        return raw with { Id = Guid.NewGuid() };
    }
}
