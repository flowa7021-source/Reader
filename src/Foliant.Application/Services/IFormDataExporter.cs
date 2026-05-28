namespace Foliant.Application.Services;

/// <summary>
/// Сериализует словарь form-данных (имя поля → значение) в текстовый формат для обмена
/// (JSON / FDF / XFDF). Stateless, без I/O — caller сам решает, куда писать результат.
/// Параллель к <see cref="IAnnotationExporter"/> и <see cref="IBookmarkExporter"/>.
/// </summary>
public interface IFormDataExporter
{
    /// <summary>Имя формата для UI (например, "JSON" / "FDF" / "XFDF").</summary>
    string FormatName { get; }

    /// <summary>Расширение файла без точки (например, "json" / "fdf" / "xfdf").</summary>
    string FileExtension { get; }

    string Export(IReadOnlyDictionary<string, string> values);
}
