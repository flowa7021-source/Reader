namespace Foliant.Application.Services;

/// <summary>
/// DI-сборка form-data exporters/importers с разрешением по расширению. Тот же поиск, что и в
/// AnnotationFormatCatalog/BookmarkFormatCatalog — намеренная триплет-копия, чтобы DI не путал
/// IEnumerable'ы (общий базовый тип сломал бы регистрацию).
/// </summary>
public sealed class FormDataFormatCatalog : IFormDataFormatCatalog
{
    public FormDataFormatCatalog(
        IEnumerable<IFormDataExporter> exporters,
        IEnumerable<IFormDataImporter> importers)
    {
        ArgumentNullException.ThrowIfNull(exporters);
        ArgumentNullException.ThrowIfNull(importers);

        Exporters = [.. exporters];
        Importers = [.. importers];
    }

    public IReadOnlyList<IFormDataExporter> Exporters { get; }

    public IReadOnlyList<IFormDataImporter> Importers { get; }

    public IFormDataExporter? ResolveExporter(string fileNameOrExtension) =>
        FindByExtension(Exporters, static e => e.FileExtension, fileNameOrExtension);

    public IFormDataImporter? ResolveImporter(string fileNameOrExtension) =>
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
