using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foliant.Application.Services;
using Foliant.Domain;

namespace Foliant.Infrastructure.Licensing;

/// <summary>
/// ECDSA-P256 / SHA-256 верификатор лицензии. Конструктор принимает PEM-encoded
/// публичный ключ (захардкоженный в release-сборке). Внутри: проверка подписи
/// над байтами JSON-а, парсинг JSON, проверка expiry.
/// </summary>
public sealed class EcdsaLicenseVerifier : ILicenseVerifier, IDisposable
{
    private readonly ECDsa _publicKey;
    private readonly HashSet<string> _revokedSignatures;

    /// <summary>Создаёт верификатор с публичным ключом издателя и опциональным блок-листом
    /// отозванных лицензий.</summary>
    /// <param name="publicKeyPem">PEM публичного ключа издателя.</param>
    /// <param name="revokedSignatureHashes">SHA-256 (hex) подписей отозванных лицензий —
    /// блок-лист §2.2, обновляется в каждом релизе. Подпись уникальна для выданной лицензии,
    /// поэтому хэш подписи идентифицирует утёкший ключ без изменения схемы лицензии. По
    /// умолчанию пусто (отзывов нет).</param>
    public EcdsaLicenseVerifier(string publicKeyPem, IEnumerable<string>? revokedSignatureHashes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyPem);
        _publicKey = ECDsa.Create();
        _publicKey.ImportFromPem(publicKeyPem);
        _revokedSignatures = new HashSet<string>(revokedSignatureHashes ?? [], StringComparer.OrdinalIgnoreCase);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Verify must always return a result; any exception during parse/verify == Invalid.")]
    public LicenseValidationResult Verify(string licenseJson, string signatureBase64, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(licenseJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(signatureBase64);

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(signatureBase64);
        }
        catch (FormatException)
        {
            return LicenseValidationResult.Invalid("Signature is not valid base64");
        }

        var data = Encoding.UTF8.GetBytes(licenseJson);
        bool sigOk;
        try
        {
            sigOk = _publicKey.VerifyData(data, signature, HashAlgorithmName.SHA256);
        }
        catch (CryptographicException ex)
        {
            return LicenseValidationResult.Invalid($"Signature verification failed: {ex.Message}");
        }

        if (!sigOk)
        {
            return LicenseValidationResult.Invalid("Signature does not match license content");
        }

        if (_revokedSignatures.Count > 0
            && _revokedSignatures.Contains(Convert.ToHexString(SHA256.HashData(signature))))
        {
            return LicenseValidationResult.Invalid("License has been revoked");
        }

        License? license;
        try
        {
            license = JsonSerializer.Deserialize(licenseJson, LicenseJsonContext.Default.License);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return LicenseValidationResult.Invalid($"License JSON malformed: {ex.Message}");
        }

        if (license is null)
        {
            return LicenseValidationResult.Invalid("License JSON deserialised to null");
        }

        return license.IsExpired(now)
            ? LicenseValidationResult.Expired(license)
            : LicenseValidationResult.Valid(license);
    }

    public void Dispose() => _publicKey.Dispose();
}

[JsonSerializable(typeof(License))]
internal sealed partial class LicenseJsonContext : JsonSerializerContext;
