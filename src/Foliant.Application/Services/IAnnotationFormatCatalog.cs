namespace Foliant.Application.Services;

/// <summary>
/// Каталог форматов обмена аннотациями (Q-F17): держит все зарегистрированные
/// <see cref="IAnnotationExporter"/>/<see cref="IAnnotationImporter"/> и разрешает нужную
/// реализацию по расширению файла. Единая точка для UI — построить фильтр диалога
/// сохранения/открытия (<c>FormatName</c> + <c>FileExtension</c>) и выбрать реализацию по
/// выбранному пользователем файлу. Stateless после конструирования, потокобезопасен на чтение.
/// </summary>
public interface IAnnotationFormatCatalog
{
    /// <summary>Все экспортёры в порядке регистрации — для построения списка форматов в UI.</summary>
    IReadOnlyList<IAnnotationExporter> Exporters { get; }

    /// <summary>Все импортёры в порядке регистрации.</summary>
    IReadOnlyList<IAnnotationImporter> Importers { get; }

    /// <summary>Найти экспортёр по расширению или имени файла (регистронезависимо; ведущая
    /// точка и полный путь допустимы, напр. <c>"json"</c>, <c>".json"</c>, <c>"notes.json"</c>).
    /// <c>null</c> — формат не поддержан.</summary>
    IAnnotationExporter? ResolveExporter(string fileNameOrExtension);

    /// <summary>Найти импортёр по расширению или имени файла. <c>null</c> — формат не поддержан.</summary>
    IAnnotationImporter? ResolveImporter(string fileNameOrExtension);
}
