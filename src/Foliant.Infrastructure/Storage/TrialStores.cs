using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foliant.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Foliant.Infrastructure.Storage;

/// <summary>
/// Тройное персистентное хранилище <see cref="TrialState"/>: DPAPI-файл
/// (primary), HKCU-реестр (secondary) и marker-файл (хэш). I/O инкапсулирован
/// здесь, чтобы <c>TrialPersistenceService</c> оставался тонким оркестратором
/// поверх чистой логики <c>TrialAntiTamperService</c>. Windows-only во время
/// выполнения (DPAPI + registry).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TrialStores
{
    // HKCU\Software\Foliant : trial-зеркало DPAPI-шифротекста (REG_BINARY).
    private const string DefaultRegistrySubKey = @"Software\Foliant";
    private const string DefaultRegistryValueName = "Trial";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        TypeInfoResolver = TrialStateJsonContext.Default,
    };

    private readonly string _primaryFile;
    private readonly string _markerFile;
    private readonly string _registrySubKey;
    private readonly string _registryValueName;
    private readonly ILogger<TrialStores> _log;

    public TrialStores(ILogger<TrialStores> log)
        : this(AppPaths.TrialFile, AppPaths.TrialMarkerFile, DefaultRegistrySubKey, DefaultRegistryValueName, log)
    {
    }

    // Ctor с явными путями — для тестов (TempDir + изолированный registry subkey).
    public TrialStores(
        string primaryFile,
        string markerFile,
        string registrySubKey,
        string registryValueName,
        ILogger<TrialStores> log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(markerFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(registrySubKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(registryValueName);
        ArgumentNullException.ThrowIfNull(log);
        _primaryFile = primaryFile;
        _markerFile = markerFile;
        _registrySubKey = registrySubKey;
        _registryValueName = registryValueName;
        _log = log;
    }

    /// <summary>Читает primary-файл (DPAPI). Отсутствие/повреждение → <c>null</c>.</summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Corrupt/foreign trial blob surfaces as absent → pure service flags tamper.")]
    public TrialState? ReadPrimary()
    {
        // FRAGILE: отсутствие файла — валидный путь (триал не запускался).
        if (!File.Exists(_primaryFile))
        {
            return null;
        }

        try
        {
            return Deserialize(Unprotect(File.ReadAllBytes(_primaryFile)));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            _log.LogWarning(ex, "Primary trial store at {Path} unreadable; treating as absent", _primaryFile);
            return null;
        }
    }

    /// <summary>Читает secondary из HKCU (DPAPI-шифротекст в REG_BINARY).</summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Registry/crypto failure surfaces as absent → pure service flags tamper.")]
    public TrialState? ReadSecondary()
    {
        try
        {
            // FRAGILE: HKCU registry чтение — Windows-only.
            using var key = Registry.CurrentUser.OpenSubKey(_registrySubKey);
            if (key?.GetValue(_registryValueName) is not byte[] cipher)
            {
                return null;
            }
            return Deserialize(Unprotect(cipher));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException
                                   or IOException or UnauthorizedAccessException
                                   or System.Security.SecurityException)
        {
            _log.LogWarning(ex, "Secondary trial store (HKCU\\{Key}) unreadable; treating as absent", _registrySubKey);
            return null;
        }
    }

    /// <summary>Читает marker-файл (plaintext-хэш). Отсутствие → <c>null</c>.</summary>
    public string? ReadMarker()
    {
        // FRAGILE: marker-файл может отсутствовать на чистой системе.
        return File.Exists(_markerFile) ? File.ReadAllText(_markerFile, Encoding.UTF8) : null;
    }

    /// <summary>Пишет <paramref name="state"/> в primary+secondary и <paramref name="marker"/> в marker-файл.</summary>
    public void WriteAll(TrialState state, string marker)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(marker);

        var cipher = Protect(Serialize(state));
        WriteFileAtomic(_primaryFile, cipher);
        WriteSecondary(cipher);
        WriteFileAtomic(_markerFile, Encoding.UTF8.GetBytes(marker));
    }

    private static byte[] Serialize(TrialState state) =>
        JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);

    private static TrialState? Deserialize(byte[] json) =>
        JsonSerializer.Deserialize(json, TrialStateJsonContext.Default.TrialState);

    // FRAGILE: DPAPI CurrentUser — расшифровать сможет только тот же Windows-юзер.
    private static byte[] Protect(byte[] plaintext) =>
        ProtectedData.Protect(plaintext, optionalEntropy: null, DataProtectionScope.CurrentUser);

    // FRAGILE: бросает CryptographicException на повреждённом/чужом шифротексте.
    private static byte[] Unprotect(byte[] cipher) =>
        ProtectedData.Unprotect(cipher, optionalEntropy: null, DataProtectionScope.CurrentUser);

    private static void WriteFileAtomic(string path, byte[] bytes)
    {
        // FRAGILE: каталог обязан существовать; AppPaths.* его создаёт при Get.
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, overwrite: true);
    }

    private void WriteSecondary(byte[] cipher)
    {
        // FRAGILE: HKCU registry запись — Windows-only; REG_BINARY шифротекст.
        using var key = Registry.CurrentUser.CreateSubKey(_registrySubKey);
        key.SetValue(_registryValueName, cipher, RegistryValueKind.Binary);
    }
}

[JsonSerializable(typeof(TrialState))]
internal sealed partial class TrialStateJsonContext : JsonSerializerContext;
