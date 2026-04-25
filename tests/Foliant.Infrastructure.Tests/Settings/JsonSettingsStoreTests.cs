using FluentAssertions;
using Foliant.Infrastructure.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Infrastructure.Tests.Settings;

public sealed class JsonSettingsStoreTests
{
    [Fact]
    public async Task Load_WhenFileMissing_ReturnsDefaults()
    {
        using var tmp = new TempDir();
        var sut = new JsonSettingsStore(tmp.File("settings.json"), NullLogger<JsonSettingsStore>.Instance);

        var result = await sut.LoadAsync(default);

        result.Should().BeEquivalentTo(AppSettings.Default);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTrips()
    {
        using var tmp = new TempDir();
        var sut = new JsonSettingsStore(tmp.File("settings.json"), NullLogger<JsonSettingsStore>.Instance);
        var s = AppSettings.Default with
        {
            Theme = "Dark",
            Language = "en",
            RecentFiles = ["a.pdf", "b.pdf"],
            Cache = new CacheSettings { DiskLimitBytes = 1024, ClearOnExit = true },
        };

        await sut.SaveAsync(s, default);
        var loaded = await sut.LoadAsync(default);

        loaded.Should().BeEquivalentTo(s);
    }

    [Fact]
    public async Task Save_AtomicViaTempFile_NoTempLeftAfterSuccess()
    {
        using var tmp = new TempDir();
        var path = tmp.File("settings.json");
        var sut = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);

        await sut.SaveAsync(AppSettings.Default, default);

        File.Exists(path).Should().BeTrue();
        File.Exists(path + ".tmp").Should().BeFalse();
    }

    [Fact]
    public async Task Load_CorruptJson_ReturnsDefaults_DoesNotThrow()
    {
        using var tmp = new TempDir();
        var path = tmp.File("settings.json");
        await File.WriteAllTextAsync(path, "{ this is not json");

        var sut = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);

        var result = await sut.LoadAsync(default);
        result.Should().BeEquivalentTo(AppSettings.Default);
    }

    [Fact]
    public async Task Load_FutureSchemaVersion_FallsBackToDefaults()
    {
        using var tmp = new TempDir();
        var path = tmp.File("settings.json");
        await File.WriteAllTextAsync(path, """{"Version": 999, "Theme": "Dark"}""");

        var sut = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);

        var result = await sut.LoadAsync(default);
        result.Should().BeEquivalentTo(AppSettings.Default);
    }

    [Fact]
    public async Task SavedFile_IsValidJson_PrettyPrinted()
    {
        using var tmp = new TempDir();
        var path = tmp.File("settings.json");
        var sut = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);

        await sut.SaveAsync(AppSettings.Default, default);

        var text = await File.ReadAllTextAsync(path);
        text.Should().Contain("\n");
        text.Should().Contain("\"Theme\":");
    }
}
