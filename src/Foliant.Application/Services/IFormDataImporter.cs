namespace Foliant.Application.Services;

/// <summary>
/// Парсит текстовый формат обмена form-данными (JSON / FDF / XFDF) в словарь имя поля → значение.
/// Stateless, без I/O. Невалидный формат верхнего уровня бросает исключение caller'у; пустые/
/// дублирующиеся записи обрабатываются на усмотрение реализации (последняя обычно выигрывает).
/// </summary>
public interface IFormDataImporter
{
    /// <summary>Имя формата для UI.</summary>
    string FormatName { get; }

    /// <summary>Расширение файла без точки.</summary>
    string FileExtension { get; }

    IReadOnlyDictionary<string, string> Import(string content);
}
