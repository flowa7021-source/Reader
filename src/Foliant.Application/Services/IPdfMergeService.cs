namespace Foliant.Application.Services;

/// <summary>
/// Объединяет несколько PDF в один: каждая страница каждого источника копируется в новый
/// документ в порядке передачи. Реализации работают over native PDFium (см. <c>IWatermarkService</c>
/// для образца pattern'а — NativeGate, atomic write).
///
/// Контракт ошибок:
/// <list type="bullet">
/// <item>Пустой список или менее двух источников → <see cref="ArgumentException"/>.</item>
/// <item>Битый PDF / IO-сбой → пробрасывается caller'у.</item>
/// </list>
///
/// Phase 1 (Q-F11, PDF-only): только PDF-источники, без переупорядочивания страниц.
/// DjVu/image/EPUB-источники и UI-reorder — следующий PR.
/// </summary>
public interface IPdfMergeService
{
    Task MergeAsync(IReadOnlyList<string> sourcePaths, string targetPath, CancellationToken ct);
}
