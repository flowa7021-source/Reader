namespace Foliant.Application.Services;

/// <summary>
/// DI-сборка всех зарегистрированных экспортёров/импортёров аннотаций с разрешением по
/// расширению файла. См. <see cref="IAnnotationFormatCatalog"/>.
/// </summary>
public sealed class AnnotationFormatCatalog : IAnnotationFormatCatalog
{
    public AnnotationFormatCatalog(
        IEnumerable<IAnnotationExporter> exporters,
        IEnumerable<IAnnotationImporter> importers)
    {
        ArgumentNullException.ThrowIfNull(exporters);
        ArgumentNullException.ThrowIfNull(importers);

        Exporters = [.. exporters];
        Importers = [.. importers];
    }

    public IReadOnlyList<IAnnotationExporter> Exporters { get; }

    public IReadOnlyList<IAnnotationImporter> Importers { get; }

    public IAnnotationExporter? ResolveExporter(string fileNameOrExtension) =>
        FindByExtension(Exporters, static e => e.FileExtension, fileNameOrExtension);

    public IAnnotationImporter? ResolveImporter(string fileNameOrExtension) =>
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

    // Принимаем "json", ".json", "notes.json", "C:\dir\notes.json", "/dir/notes.json".
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
