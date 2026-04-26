using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Foliant.Infrastructure.Settings;

public sealed class JsonSettingsStore(string filePath, ILogger<JsonSettingsStore> log) : ISettingsStore
{
    private readonly string _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = SettingsJsonContext.Default,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        TypeInfoResolver = SettingsJsonContext.Default,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public async Task<AppSettings> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
        {
            log.LogInformation("Settings file not found at {Path}, using defaults", _filePath);
            return AppSettings.Default;
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var loaded = await JsonSerializer
                .DeserializeAsync<AppSettings>(stream, ReadOptions, ct)
                .ConfigureAwait(false);

            return SettingsMigrator.Migrate(loaded ?? AppSettings.Default);
        }
        catch (JsonException ex)
        {
            log.LogWarning(ex, "Settings file corrupt at {Path}, fallback to defaults", _filePath);
            return AppSettings.Default;
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var tmp = _filePath + ".tmp";

        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, settings, WriteOptions, ct).ConfigureAwait(false);
        }

        File.Move(tmp, _filePath, overwrite: true);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(CacheSettings))]
[JsonSerializable(typeof(OcrSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
