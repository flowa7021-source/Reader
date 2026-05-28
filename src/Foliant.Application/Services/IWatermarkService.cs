using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Накладывает текстовый watermark на каждую страницу PDF и пишет результат в новый файл.
/// Реализации работают over native PDFium (см. AnnotatedPdfExportService для образца
/// pattern'а — NativeGate, atomic write).
///
/// Контракт ошибок:
/// <list type="bullet">
/// <item>Пустой <see cref="WatermarkSpec.Text"/> / opacity вне [0,1] / отрицательный font size →
/// <see cref="ArgumentException"/>.</item>
/// <item>Битый PDF / IO-сбой → пробрасывается caller'у.</item>
/// </list>
///
/// Phase 1 (Q-F13): only text. Image-watermark и per-range — следующий PR.
/// </summary>
public interface IWatermarkService
{
    Task ApplyAsync(string sourcePath, WatermarkSpec spec, string targetPath, CancellationToken ct);
}
