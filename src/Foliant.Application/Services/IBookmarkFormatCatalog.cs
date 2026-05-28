namespace Foliant.Application.Services;

/// <summary>
/// Каталог форматов обмена закладками: держит все зарегистрированные
/// <see cref="IBookmarkExporter"/>/<see cref="IBookmarkImporter"/> и разрешает реализацию по
/// расширению файла. Параллель к <see cref="IAnnotationFormatCatalog"/> — UI строит фильтр
/// диалога из <c>FormatName</c>+<c>FileExtension</c> и берёт нужную реализацию по выбранному
/// пользователем файлу. Stateless после конструирования, потокобезопасен на чтение.
/// </summary>
public interface IBookmarkFormatCatalog
{
    /// <summary>Все экспортёры в порядке регистрации.</summary>
    IReadOnlyList<IBookmarkExporter> Exporters { get; }

    /// <summary>Все импортёры в порядке регистрации.</summary>
    IReadOnlyList<IBookmarkImporter> Importers { get; }

    /// <summary>Найти экспортёр по расширению или имени файла (регистронезависимо; ведущая
    /// точка и полный путь допустимы). <c>null</c> — формат не поддержан.</summary>
    IBookmarkExporter? ResolveExporter(string fileNameOrExtension);

    /// <summary>Найти импортёр по расширению или имени файла. <c>null</c> — формат не поддержан.</summary>
    IBookmarkImporter? ResolveImporter(string fileNameOrExtension);
}
