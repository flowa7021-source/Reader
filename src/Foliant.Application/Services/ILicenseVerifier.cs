using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Верифицирует пару (license-JSON, base64-подпись) против захардкоженного
/// публичного ECDSA-ключа. Никакого I/O — чистая криптопроверка + парсинг.
/// </summary>
public interface ILicenseVerifier
{
    LicenseValidationResult Verify(string licenseJson, string signatureBase64, DateTimeOffset now);
}
