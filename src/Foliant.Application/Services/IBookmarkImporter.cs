using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Парсит текстовый формат обмена закладками (JSON и т.п.) в список <see cref="Bookmark"/>.
/// Параллель к <see cref="IAnnotationImporter"/>: stateless, без I/O; неузнанные/битые элементы
/// пропускаются (best-effort), ошибка формата верхнего уровня пробрасывается вызывающему.
/// </summary>
public interface IBookmarkImporter
{
    /// <summary>Имя формата для UI (например, "JSON").</summary>
    string FormatName { get; }

    /// <summary>Расширение файла без точки (например, "json").</summary>
    string FileExtension { get; }

    /// <summary>Разобрать <paramref name="content"/> в закладки. <c>Id</c> генерируются заново —
    /// формат обмена их не несёт как идентичность.</summary>
    IReadOnlyList<Bookmark> Import(string content);
}
