using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Infrastructure.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Infrastructure.Tests.Settings;

public sealed class RecentsServiceTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (IDisposable d in _disposables)
        {
            d.Dispose();
        }
        _tmp.Dispose();
    }

    // Recents теперь мутирует настройки через ISettingsService (единая точка записи), поэтому
    // SUT собирается поверх реального SettingsService + JsonSettingsStore.
    private RecentsService CreateSut(string settingsFileName = "settings.json")
    {
        var store = new JsonSettingsStore(_tmp.File(settingsFileName), NullLogger<JsonSettingsStore>.Instance);
        var settings = new SettingsService(store, NullLogger<SettingsService>.Instance);
        _disposables.Add(settings);
        return new RecentsService(settings, NullLogger<RecentsService>.Instance);
    }

    [Fact]
    public async Task Get_OnEmptyStore_ReturnsEmpty()
    {
        var sut = CreateSut();

        var result = await sut.GetAsync(default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Add_NewPath_AppearsAtFront()
    {
        var sut = CreateSut();
        var path = _tmp.File("a.pdf");

        await sut.AddAsync(path, default);

        var result = await sut.GetAsync(default);
        result.Should().HaveCount(1);
        result[0].Should().Be(Path.GetFullPath(path));
    }

    [Fact]
    public async Task Add_ExistingPath_MovesItToFront_DoesNotDuplicate()
    {
        var sut = CreateSut();
        var a = _tmp.File("a.pdf");
        var b = _tmp.File("b.pdf");

        await sut.AddAsync(a, default);
        await sut.AddAsync(b, default);
        await sut.AddAsync(a, default);

        var result = await sut.GetAsync(default);
        result.Should().HaveCount(2);
        result[0].Should().Be(Path.GetFullPath(a));
        result[1].Should().Be(Path.GetFullPath(b));
    }

    [Fact]
    public async Task Add_RespectsMaxItemsCap()
    {
        var sut = CreateSut();

        for (int i = 0; i < IRecentsService.MaxItems + 5; i++)
        {
            await sut.AddAsync(_tmp.File($"f{i}.pdf"), default);
        }

        var result = await sut.GetAsync(default);
        result.Count.Should().Be(IRecentsService.MaxItems);
        result[0].Should().EndWith($"f{IRecentsService.MaxItems + 4}.pdf");
    }

    [Fact]
    public async Task Add_DedupesCaseInsensitive()
    {
        var sut = CreateSut();
        var lower = _tmp.File("doc.pdf");
        var upper = _tmp.File("DOC.PDF");

        await sut.AddAsync(lower, default);
        await sut.AddAsync(upper, default);

        var result = await sut.GetAsync(default);
        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task Remove_ExistingPath_RemovesIt()
    {
        var sut = CreateSut();
        var a = _tmp.File("a.pdf");
        var b = _tmp.File("b.pdf");
        await sut.AddAsync(a, default);
        await sut.AddAsync(b, default);

        await sut.RemoveAsync(a, default);

        var result = await sut.GetAsync(default);
        result.Should().HaveCount(1);
        result[0].Should().Be(Path.GetFullPath(b));
    }

    [Fact]
    public async Task Remove_NonExistingPath_IsNoOp()
    {
        var sut = CreateSut();
        var a = _tmp.File("a.pdf");
        await sut.AddAsync(a, default);

        await sut.RemoveAsync(_tmp.File("does-not-exist.pdf"), default);

        var result = await sut.GetAsync(default);
        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task Clear_RemovesAll()
    {
        var sut = CreateSut();
        await sut.AddAsync(_tmp.File("a.pdf"), default);
        await sut.AddAsync(_tmp.File("b.pdf"), default);

        await sut.ClearAsync(default);

        (await sut.GetAsync(default)).Should().BeEmpty();
    }

    [Fact]
    public async Task RecentFiles_Persist_AcrossInstances()
    {
        var settingsPath = _tmp.File("persist.json");

        var store1 = new JsonSettingsStore(settingsPath, NullLogger<JsonSettingsStore>.Instance);
        using (var settings1 = new SettingsService(store1, NullLogger<SettingsService>.Instance))
        {
            var sut1 = new RecentsService(settings1, NullLogger<RecentsService>.Instance);
            await sut1.AddAsync(_tmp.File("doc.pdf"), default);
        }

        var store2 = new JsonSettingsStore(settingsPath, NullLogger<JsonSettingsStore>.Instance);
        using var settings2 = new SettingsService(store2, NullLogger<SettingsService>.Instance);
        await settings2.LoadAsync(default);
        var sut2 = new RecentsService(settings2, NullLogger<RecentsService>.Instance);
        var result = await sut2.GetAsync(default);

        result.Should().HaveCount(1);
        result[0].Should().Be(Path.GetFullPath(_tmp.File("doc.pdf")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Add_BadPath_Throws(string? path)
    {
        var sut = CreateSut();

        var act = async () => await sut.AddAsync(path!, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // Regression: одновременная запись recents и других настроек не должна терять поля.
    // Раньше RecentsService писал через ISettingsStore напрямую (read-modify-write поверх
    // диска), затирая тему/кэш, изменённые через SettingsService. Теперь — общий UpdateAsync.
    [Fact]
    public async Task Add_PreservesUnrelatedSettings_NoLostUpdate()
    {
        var store = new JsonSettingsStore(_tmp.File("shared.json"), NullLogger<JsonSettingsStore>.Instance);
        using var settings = new SettingsService(store, NullLogger<SettingsService>.Instance);
        var recents = new RecentsService(settings, NullLogger<RecentsService>.Instance);

        await settings.UpdateAsync(s => s with { Theme = "Dark" }, default);
        await recents.AddAsync(_tmp.File("a.pdf"), default);

        settings.Current.Theme.Should().Be("Dark");
        settings.Current.RecentFiles.Should().ContainSingle();
    }
}
