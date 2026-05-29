namespace Foliant.Application.Services;

/// <summary>
/// Применяет словарь form-данных (имя поля → значение) к PDF и пишет результат в новый файл.
/// Реализации работают over native PDFium (см. <c>IWatermarkService</c> для образца pattern'а
/// — NativeGate, atomic write). Парный сервис к read-only <see cref="IPdfFormReader"/>.
///
/// Контракт ошибок:
/// <list type="bullet">
/// <item><c>null</c>/whitespace путь или <c>null</c> values → <see cref="ArgumentException"/>.</item>
/// <item>Битый PDF / IO-сбой → пробрасывается caller'у.</item>
/// <item>Имена полей, отсутствующие в исходном PDF, игнорируются (best-effort, как у
/// <see cref="IPdfFormReader"/>); это позволяет применять «один FDF на пачку PDF» без
/// падений на не совпадающих полях.</item>
/// </list>
///
/// Phase 1 (Q-F24 write-back): только write of <c>/V</c> entry на widget-аннотациях — это
/// читается <see cref="IPdfFormReader"/> и любым PDF-reader'ом. Visual appearance
/// (regenerate <c>/AP</c>) — follow-up; стандартные viewer'ы пересчитывают её сами при
/// <c>/NeedAppearances true</c>.
/// </summary>
public interface IPdfFormFillService
{
    Task ApplyAsync(
        string sourcePath,
        IReadOnlyDictionary<string, string> values,
        string targetPath,
        CancellationToken ct);
}
