using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Применяет <see cref="CropSpec"/> ко всем страницам PDF и пишет результат в новый файл.
/// Реализации работают over native PDFium (см. <c>IWatermarkService</c> для образца pattern'а
/// — NativeGate, atomic write).
///
/// Контракт ошибок:
/// <list type="bullet">
/// <item>Спек с <c>HasEffect == false</c> или провалившая <see cref="CropSpec.Validate"/> →
/// <see cref="ArgumentException"/>.</item>
/// <item>Битый PDF / IO-сбой → пробрасывается caller'у.</item>
/// </list>
///
/// Phase 1 (Q-F15): только обратимый CropBox, единая spec на весь документ. Физический crop
/// и per-range — следующий PR.
/// </summary>
public interface IPdfCropService
{
    Task ApplyAsync(string sourcePath, CropSpec spec, string targetPath, CancellationToken ct);
}
