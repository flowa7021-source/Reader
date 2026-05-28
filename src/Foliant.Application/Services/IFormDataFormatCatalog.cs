namespace Foliant.Application.Services;

/// <summary>
/// Каталог форматов обмена form-данными (Q-F24). Параллель к
/// <see cref="IAnnotationFormatCatalog"/>/<see cref="IBookmarkFormatCatalog"/>: держит все
/// зарегистрированные экспортёры/импортёры и разрешает реализацию по расширению файла.
/// </summary>
public interface IFormDataFormatCatalog
{
    IReadOnlyList<IFormDataExporter> Exporters { get; }

    IReadOnlyList<IFormDataImporter> Importers { get; }

    /// <summary>Найти экспортёр по расширению / имени файла / полному пути. <c>null</c> — формат
    /// не поддержан.</summary>
    IFormDataExporter? ResolveExporter(string fileNameOrExtension);

    /// <summary>Найти импортёр аналогично. <c>null</c> — формат не поддержан.</summary>
    IFormDataImporter? ResolveImporter(string fileNameOrExtension);
}
