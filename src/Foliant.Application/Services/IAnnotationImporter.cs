using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Парсит текстовый формат обмена аннотациями (XFDF и т.п.) в список <see cref="Annotation"/>.
/// Stateless, без I/O — caller сам читает содержимое и решает, как мержить результат.
/// Неузнанные/битые элементы пропускаются (best-effort); ошибка формата верхнего уровня
/// (невалидный XML) пробрасывается вызывающему.
/// </summary>
public interface IAnnotationImporter
{
    /// <summary>Имя формата для UI (например, "XFDF").</summary>
    string FormatName { get; }

    /// <summary>Расширение файла без точки (например, "xfdf").</summary>
    string FileExtension { get; }

    /// <summary>Разобрать <paramref name="content"/> в аннотации. <c>Id</c> генерируются заново —
    /// формат обмена их не несёт.</summary>
    IReadOnlyList<Annotation> Import(string content);
}
