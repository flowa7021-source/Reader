using System.Text.Json;
using System.Text.Json.Serialization;
using Foliant.Domain;

namespace Foliant.Application.Services;

public sealed class JsonBookmarkExporter : IBookmarkExporter
{
    public string FormatName => "JSON";

    public string FileExtension => "json";

    public string Export(IReadOnlyList<Bookmark> bookmarks)
    {
        ArgumentNullException.ThrowIfNull(bookmarks);
        return JsonSerializer.Serialize(bookmarks, BookmarkExportJsonContext.Default.IReadOnlyListBookmark);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(IReadOnlyList<Bookmark>))]
[JsonSerializable(typeof(Bookmark))]
internal sealed partial class BookmarkExportJsonContext : JsonSerializerContext;
