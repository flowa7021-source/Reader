using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foliant.Application.Services;
using Foliant.Domain;
using Foliant.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Foliant.Infrastructure.Licensing;

/// <summary>
/// Production <see cref="ILicenseStorage"/>: сериализует <see cref="LicenseBlob"/>
/// в JSON, шифрует через DPAPI (<see cref="DataProtectionScope.CurrentUser"/>) и
/// пишет в <see cref="AppPaths.LicenseFile"/> атомарно (<c>.tmp</c> + Move). Копия
/// шифротекста дублируется в реестр <c>HKCU\Software\Foliant</c> как защита от
/// удаления файла. Отсутствие обоих источников == «нет лицензии» (<c>null</c>).
/// Windows-only во время выполнения (DPAPI + registry).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiLicenseStorage : ILicenseStorage
{
    // HKCU\Software\Foliant : license-зеркало шифротекста (base64).
    private const string RegistrySubKey = @"Software\Foliant";
    private const string RegistryValueName = "License";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        TypeInfoResolver = LicenseBlobJsonContext.Default,
    };

    private readonly string _filePath;
    private readonly string _registrySubKey;
    private readonly string _registryValueName;
    private readonly ILogger<DpapiLicenseStorage> _log;

    public DpapiLicenseStorage(ILogger<DpapiLicenseStorage> log)
        : this(AppPaths.LicenseFile, RegistrySubKey, RegistryValueName, log)
    {
    }

    // Ctor с явными путями — для тестов (TempDir + изолированный registry subkey).
    public DpapiLicenseStorage(
        string filePath,
        string registrySubKey,
        string registryValueName,
        ILogger<DpapiLicenseStorage> log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(registrySubKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(registryValueName);
        ArgumentNullException.ThrowIfNull(log);
        _filePath = filePath;
        _registrySubKey = registrySubKey;
        _registryValueName = registryValueName;
        _log = log;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Corrupt/foreign-user ciphertext must surface as 'no license', not crash startup.")]
    public Task<LicenseBlob?> LoadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var cipher = ReadCipherFromFile() ?? ReadCipherFromRegistry();
        if (cipher is null)
        {
            return Task.FromResult<LicenseBlob?>(null);
        }

        try
        {
            var json = Unprotect(cipher);
            var blob = JsonSerializer.Deserialize(json, LicenseBlobJsonContext.Default.LicenseBlob);
            return Task.FromResult(blob);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            _log.LogWarning(ex, "License ciphertext at {Path} unreadable; treating as absent", _filePath);
            return Task.FromResult<LicenseBlob?>(null);
        }
    }

    public Task SaveAsync(LicenseBlob blob, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(blob);
        ct.ThrowIfCancellationRequested();

        var json = JsonSerializer.SerializeToUtf8Bytes(blob, WriteOptions);
        var cipher = Protect(json);

        WriteFileAtomic(cipher);
        WriteRegistry(cipher);
        _log.LogInformation("License persisted to {Path} and registry mirror", _filePath);
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
        DeleteRegistry();
        return Task.CompletedTask;
    }

    // FRAGILE: DPAPI CurrentUser — расшифровать сможет только тот же Windows-юзер.
    private static byte[] Protect(byte[] plaintext) =>
        ProtectedData.Protect(plaintext, optionalEntropy: null, DataProtectionScope.CurrentUser);

    // FRAGILE: бросает CryptographicException на повреждённом/чужом шифротексте.
    private static string Unprotect(byte[] cipher)
    {
        var plain = ProtectedData.Unprotect(cipher, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plain);
    }

    private byte[]? ReadCipherFromFile()
    {
        // FRAGILE: файл может отсутствовать (нет лицензии) — это валидный путь.
        return File.Exists(_filePath) ? File.ReadAllBytes(_filePath) : null;
    }

    private void WriteFileAtomic(byte[] cipher)
    {
        // FRAGILE: каталог обязан существовать до Create; AppPaths.RoamingAppData его создаёт.
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var tmp = _filePath + ".tmp";
        File.WriteAllBytes(tmp, cipher);
        File.Move(tmp, _filePath, overwrite: true);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Registry mirror is best-effort; file remains the source of truth.")]
    private byte[]? ReadCipherFromRegistry()
    {
        try
        {
            // FRAGILE: HKCU registry чтение — Windows-only.
            using var key = Registry.CurrentUser.OpenSubKey(_registrySubKey);
            return key?.GetValue(_registryValueName) as byte[];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _log.LogWarning(ex, "Registry license mirror unreadable under HKCU\\{Key}", _registrySubKey);
            return null;
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Registry mirror is best-effort; failure must not block a successful file write.")]
    private void WriteRegistry(byte[] cipher)
    {
        try
        {
            // FRAGILE: HKCU registry запись — Windows-only; REG_BINARY шифротекст.
            using var key = Registry.CurrentUser.CreateSubKey(_registrySubKey);
            key.SetValue(_registryValueName, cipher, RegistryValueKind.Binary);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _log.LogWarning(ex, "Could not mirror license into HKCU\\{Key}", _registrySubKey);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Best-effort cleanup of the registry mirror; absence is acceptable.")]
    private void DeleteRegistry()
    {
        try
        {
            // FRAGILE: HKCU registry удаление — Windows-only; отсутствие значения == no-op.
            using var key = Registry.CurrentUser.OpenSubKey(_registrySubKey, writable: true);
            key?.DeleteValue(_registryValueName, throwOnMissingValue: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _log.LogWarning(ex, "Could not clear license mirror under HKCU\\{Key}", _registrySubKey);
        }
    }
}

[JsonSerializable(typeof(LicenseBlob))]
internal sealed partial class LicenseBlobJsonContext : JsonSerializerContext;
