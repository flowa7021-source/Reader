namespace Foliant.Application.Services;

/// <summary>
/// Лёгкий fingerprint файла для ключей кэша/индекса/sidecar-файлов.
/// См. IMPLEMENTATION_PLAN.md, раздел 5.1.
/// </summary>
public interface IFileFingerprint
{
    /// <summary>Hex-string без префикса. Стабилен между процессами.</summary>
    Task<string> ComputeAsync(string path, CancellationToken ct);
}
