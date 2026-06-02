using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Физический redaction PDF: для каждой <see cref="RedactionRegion"/> удаляет текстовые объекты
/// страницы, чьи bbox пересекают прямоугольник (текст реально исчезает из контента и текстового
/// слоя), и рисует поверх области непрозрачный чёрный бокс. Результат пишется в новый файл —
/// оригинал не мутируется (как watermark/header-footer сервисы). Реализации работают over native
/// PDFium (см. <see cref="IWatermarkService"/> для образца pattern'а — NativeGate, atomic write).
///
/// Контракт ошибок:
/// <list type="bullet">
/// <item>Пустые / blank пути → <see cref="System.ArgumentException"/>.</item>
/// <item><see cref="RedactionRegion.PageIndex"/> вне диапазона страниц →
/// <see cref="System.ArgumentOutOfRangeException"/>.</item>
/// <item>Битый PDF / IO-сбой → пробрасывается caller'у.</item>
/// </list>
///
/// MVP (Q-F32 partial): только координатные области + удаление пересекающего текста + чёрный бокс.
/// Find-and-redact по тексту/regex, удаление изображений, метаданные, OCG — follow-up.
/// </summary>
public interface IRedactionService
{
    Task RedactAsync(string sourcePath, string outputPath, IReadOnlyList<RedactionRegion> regions, CancellationToken ct);
}
