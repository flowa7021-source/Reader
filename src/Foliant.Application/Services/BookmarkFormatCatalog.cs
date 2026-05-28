namespace Foliant.Application.Services;

/// <summary>
/// DI-сборка всех зарегистрированных экспортёров/импортёров закладок с разрешением по
/// расширению файла. Параллель к <see cref="AnnotationFormatCatalog"/>; реализация общего
/// поиска по расширению повторяется намеренно — каталоги не имеют общего базового типа,
/// чтобы DI-resolution не путал коллекции.
/// </summary>
public sealed class BookmarkFormatCatalog : IBookmarkFormatCatalog
{
    public BookmarkFormatCatalog(
        IEnumerable<IBookmarkExporter> exporters,
        IEnumerable<IBookmarkImporter> importers)
    {
        ArgumentNullException.ThrowIfNull(exporters);
        ArgumentNullException.ThrowIfNull(importers);

        Exporters = [.. exporters];
        Importers = [.. importers];
    }

    public IReadOnlyList<IBookmarkExporter> Exporters { get; }

    public IReadOnlyList<IBookmarkImporter> Importers { get; }

    public IBookmarkExporter? ResolveExporter(string fileNameOrExtension) =>
        FindByExtension(Exporters, static e => e.FileExtension, fileNameOrExtension);

    public IBookmarkImporter? ResolveImporter(string fileNameOrExtension) =>
        FindByExtension(Importers, static i => i.FileExtension, fileNameOrExtension);

    private static T? FindByExtension<T>(IReadOnlyList<T> items, Func<T, string> extensionOf, string fileNameOrExtension)
        where T : class
    {
        string ext = NormalizeExtension(fileNameOrExtension);
        if (ext.Length == 0)
        {
            return null;
        }

        foreach (var item in items)
        {
            if (string.Equals(NormalizeExtension(extensionOf(item)), ext, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }

    // Принимаем "json", ".json", "bookmarks.json", "C:\dir\bookmarks.json", "/dir/bookmarks.json".
    private static string NormalizeExtension(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string name = value.Trim();
        int slash = name.LastIndexOfAny(['/', '\\']);
        if (slash >= 0)
        {
            name = name[(slash + 1)..];
        }

        int dot = name.LastIndexOf('.');
        string ext = dot >= 0 ? name[(dot + 1)..] : name;
        return ext.Trim();
    }
}
