namespace Foliant.Infrastructure.Storage;

/// <summary>
/// Лёгкий fingerprint файла для ключей кэша. См. IMPLEMENTATION_PLAN.md, раздел 5.1.
/// Раз в N открытий — возможно фоновое вычисление полного хэша всего файла.
/// </summary>
public interface IFileFingerprint
{
    /// <summary>Hex-string без префикса. Стабилен между процессами.</summary>
    Task<string> ComputeAsync(string path, CancellationToken ct);
}
