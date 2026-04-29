using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Infrastructure.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Infrastructure.Tests.Settings;

public sealed class RecentsServiceTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose()
    {
        _tmp.Dispose();
    }

    private RecentsService CreateSut(string settingsFileName = "settings.json")
    {
        var store = new JsonSettingsStore(_tmp.File(settingsFileName), NullLogger<JsonSettingsStore>.Instance);
        return new RecentsService(store, NullLogger<RecentsService>.Instance);
    }

    [Fact]
    public async Task Get_OnEmptyStore_ReturnsEmpty()
    {
        using var sut = CreateSut();

        var result = await sut.GetAsync(default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Add_NewPath_AppearsAtFront()
    {
        using var sut = CreateSut();
        var path = _tmp.File("a.pdf");

        await sut.AddAsync(path, default);

        var result = await sut.GetAsync(default);
        result.Should().HaveCount(1);
        result[0].Should().Be(Path.GetFullPath(path));
    }

    [Fact]
    public async Task Add_ExistingPath_MovesItToFront_DoesNotDuplicate()
    {
        using var sut = CreateSut();
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
        using var sut = CreateSut();

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
        using var sut = CreateSut();
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
        using var sut = CreateSut();
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
        using var sut = CreateSut();
        var a = _tmp.File("a.pdf");
        await sut.AddAsync(a, default);

        await sut.RemoveAsync(_tmp.File("does-not-exist.pdf"), default);

        var result = await sut.GetAsync(default);
        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task Clear_RemovesAll()
    {
        using var sut = CreateSut();
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
        using (var sut1 = new RecentsService(store1, NullLogger<RecentsService>.Instance))
        {
            await sut1.AddAsync(_tmp.File("doc.pdf"), default);
        }

        var store2 = new JsonSettingsStore(settingsPath, NullLogger<JsonSettingsStore>.Instance);
        using var sut2 = new RecentsService(store2, NullLogger<RecentsService>.Instance);
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
        using var sut = CreateSut();

        var act = async () => await sut.AddAsync(path!, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
