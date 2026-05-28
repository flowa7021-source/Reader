namespace Foliant.Application.Services;

/// <summary>
/// Читает значения AcroForm-полей из PDF на диске. Stateless; ошибки IO/парсинга пробрасываются
/// caller'у. Для PDF без AcroForm возвращает пустой словарь (не исключение).
///
/// Phase 1 — read-only. Запись значений обратно в PDF — отдельный сервис, поскольку требует
/// другого набора PDFium-вызовов и регенерации appearance streams.
/// </summary>
public interface IPdfFormReader
{
    /// <summary>Прочитать все form-поля. Ключ — имя поля (PartialName из PDF), значение —
    /// строковое представление текущего value (для checkbox: "Yes"/"Off"; для text: содержимое;
    /// для unsupported types — пустая строка). Порядок результата — не гарантирован.</summary>
    Task<IReadOnlyDictionary<string, string>> ReadAsync(string pdfPath, CancellationToken ct);
}
