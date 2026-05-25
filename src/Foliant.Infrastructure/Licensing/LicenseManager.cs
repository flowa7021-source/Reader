using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.Infrastructure.Licensing;

public sealed class LicenseManager(
    ILicenseStorage storage,
    ILicenseVerifier verifier,
    TimeProvider clock,
    ILogger<LicenseManager> log) : ILicenseManager
{
    public async Task<LicenseValidationResult> CurrentAsync(CancellationToken ct)
    {
        var blob = await storage.LoadAsync(ct).ConfigureAwait(false);
        if (blob is null)
        {
            return LicenseValidationResult.Missing;
        }

        var verdict = verifier.Verify(blob.LicenseJson, blob.SignatureBase64, clock.GetUtcNow());
        log.LogDebug("License current status: {Status}", verdict.Status);
        return verdict;
    }

    public async Task<LicenseValidationResult> ImportAsync(
        string licenseJson, string signatureBase64, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(licenseJson);
        ArgumentNullException.ThrowIfNull(signatureBase64);

        var verdict = verifier.Verify(licenseJson, signatureBase64, clock.GetUtcNow());
        if (verdict.Status != LicenseStatus.Valid)
        {
            log.LogWarning("License import rejected: {Status} ({Reason})", verdict.Status, verdict.Reason);
            return verdict;
        }

        await storage.SaveAsync(new LicenseBlob(licenseJson, signatureBase64), ct).ConfigureAwait(false);
        log.LogInformation("License imported for user {User}", verdict.License!.User);
        return verdict;
    }

    public Task ClearAsync(CancellationToken ct) => storage.ClearAsync(ct);
}
