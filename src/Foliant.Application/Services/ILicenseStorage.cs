using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Хранение лицензии. Production-impl шифрует через DPAPI (CurrentUser scope)
/// и пишет в <c>%APPDATA%/Foliant/license.key</c>; in-memory test impl —
/// просто словарь. Возвращает <c>null</c>, когда файл отсутствует.
/// </summary>
public interface ILicenseStorage
{
    Task<LicenseBlob?> LoadAsync(CancellationToken ct);

    Task SaveAsync(LicenseBlob blob, CancellationToken ct);

    Task ClearAsync(CancellationToken ct);
}
