using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Фасад над <see cref="ILicenseStorage"/> + <see cref="ILicenseVerifier"/>.
/// Текущая лицензия валидируется при каждом запросе — повторных кэшей нет,
/// чтобы устаревший статус не «заклинило».
/// </summary>
public interface ILicenseManager
{
    /// <summary>Возвращает текущий вердикт по сохранённой лицензии (или <c>Missing</c>).</summary>
    Task<LicenseValidationResult> CurrentAsync(CancellationToken ct);

    /// <summary>
    /// Принимает пользовательский ввод (lic-JSON + base64-подпись), верифицирует;
    /// если <c>Valid</c> — сохраняет в storage. На любом другом статусе ничего
    /// не пишет и просто возвращает результат.
    /// </summary>
    Task<LicenseValidationResult> ImportAsync(string licenseJson, string signatureBase64, CancellationToken ct);

    Task ClearAsync(CancellationToken ct);
}
