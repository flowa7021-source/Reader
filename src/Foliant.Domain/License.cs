namespace Foliant.Domain;

/// <summary>
/// Распарсенная (но ещё не верифицированная) лицензия. Подписывается ECDSA-P256
/// издателем; публичный ключ захардкожен в <c>EcdsaLicenseVerifier</c>.
/// </summary>
public sealed record License(
    string User,
    string Sku,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<string> Features)
{
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>Регистро-нечувствительная проверка наличия фичи в лицензии.</summary>
    public bool HasFeature(string featureCode)
    {
        ArgumentNullException.ThrowIfNull(featureCode);
        foreach (var f in Features)
        {
            if (string.Equals(f, featureCode, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}

public enum LicenseStatus
{
    Valid,
    Expired,
    Invalid,
    Missing,
}

public sealed record LicenseValidationResult(LicenseStatus Status, License? License, string? Reason)
{
    public static LicenseValidationResult Valid(License license) =>
        new(LicenseStatus.Valid, license, null);

    public static LicenseValidationResult Expired(License license) =>
        new(LicenseStatus.Expired, license, $"License expired at {license.ExpiresAt:O}");

    public static LicenseValidationResult Invalid(string reason) =>
        new(LicenseStatus.Invalid, null, reason);

    public static LicenseValidationResult Missing { get; } =
        new(LicenseStatus.Missing, null, "No license file present");
}
