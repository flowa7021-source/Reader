using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Сериализует список <see cref="Annotation"/> в текстовый формат для
/// экспорта (JSON / Markdown / etc.). Реализации stateless, чистый код,
/// никаких I/O — caller сам пишет результат туда, куда нужно.
/// </summary>
public interface IAnnotationExporter
{
    /// <summary>Имя формата для UI (например, "JSON" / "Markdown").</summary>
    string FormatName { get; }

    /// <summary>Расширение файла без точки (например, "json" / "md").</summary>
    string FileExtension { get; }

    string Export(IReadOnlyList<Annotation> annotations);
}
