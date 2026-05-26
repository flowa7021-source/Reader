using FluentAssertions;
using Foliant.Application.Settings;
using Foliant.Infrastructure.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Infrastructure.Tests.Settings;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose()
    {
        _tmp.Dispose();
    }

    private SettingsService CreateSut()
    {
        var store = new JsonSettingsStore(_tmp.File("settings.json"), NullLogger<JsonSettingsStore>.Instance);
        return new SettingsService(store, NullLogger<SettingsService>.Instance);
    }

    [Fact]
    public void Current_BeforeLoad_ReturnsDefault()
    {
        using var sut = CreateSut();

        sut.Current.Should().BeEquivalentTo(AppSettings.Default);
    }

    [Fact]
    public async Task LoadAsync_PopulatesCurrent()
    {
        var store = new JsonSettingsStore(_tmp.File("settings.json"), NullLogger<JsonSettingsStore>.Instance);
        var saved = AppSettings.Default with { Theme = "Dark", Language = "en" };
        await store.SaveAsync(saved, default);

        using var sut = new SettingsService(store, NullLogger<SettingsService>.Instance);
        await sut.LoadAsync(default);

        sut.Current.Theme.Should().Be("Dark");
        sut.Current.Language.Should().Be("en");
    }

    [Fact]
    public async Task SaveAsync_UpdatesCurrent_AndPersists()
    {
        using var sut = CreateSut();
        var updated = AppSettings.Default with { Theme = "HighContrast" };

        await sut.SaveAsync(updated, default);

        sut.Current.Theme.Should().Be("HighContrast");

        // Reload a fresh instance to verify persistence.
        var store2 = new JsonSettingsStore(_tmp.File("settings.json"), NullLogger<JsonSettingsStore>.Instance);
        using var sut2 = new SettingsService(store2, NullLogger<SettingsService>.Instance);
        await sut2.LoadAsync(default);
        sut2.Current.Theme.Should().Be("HighContrast");
    }

    [Fact]
    public async Task SaveAsync_NullSettings_Throws()
    {
        using var sut = CreateSut();

        var act = async () => await sut.SaveAsync(null!, default);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task UpdateAsync_AppliesMutation_UpdatesCurrent_AndPersists()
    {
        using var sut = CreateSut();

        await sut.UpdateAsync(s => s with { Theme = "Dark" }, default);

        sut.Current.Theme.Should().Be("Dark");

        var store2 = new JsonSettingsStore(_tmp.File("settings.json"), NullLogger<JsonSettingsStore>.Instance);
        using var sut2 = new SettingsService(store2, NullLogger<SettingsService>.Instance);
        await sut2.LoadAsync(default);
        sut2.Current.Theme.Should().Be("Dark");
    }

    [Fact]
    public async Task UpdateAsync_MutatorReturnsSameInstance_DoesNotPersist()
    {
        var store = new JsonSettingsStore(_tmp.File("settings.json"), NullLogger<JsonSettingsStore>.Instance);
        using var sut = new SettingsService(store, NullLogger<SettingsService>.Instance);

        await sut.UpdateAsync(s => s, default);

        // Никакой записи не было — файл не создан, перезагрузка даёт дефолт.
        File.Exists(_tmp.File("settings.json")).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_SequentialMutations_PreserveEachOther()
    {
        using var sut = CreateSut();

        await sut.UpdateAsync(s => s with { Theme = "Dark" }, default);
        await sut.UpdateAsync(s => s with { RecentFiles = ["a.pdf"] }, default);

        sut.Current.Theme.Should().Be("Dark");
        sut.Current.RecentFiles.Should().ContainSingle().Which.Should().Be("a.pdf");
    }

    [Fact]
    public async Task UpdateAsync_NullMutator_Throws()
    {
        using var sut = CreateSut();

        var act = async () => await sut.UpdateAsync(null!, default);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
