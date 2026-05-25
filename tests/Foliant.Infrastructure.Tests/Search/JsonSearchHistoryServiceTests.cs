using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Infrastructure.Search;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Infrastructure.Tests.Search;

public sealed class JsonSearchHistoryServiceTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private JsonSearchHistoryService MakeSut(int maxItems = 10) =>
        new(Path.Combine(_tmp.Path, "history.json"),
            NullLogger<JsonSearchHistoryService>.Instance,
            maxItems);

    // ───── S6/E ─────

    [Fact]
    public void FreshInstance_GetHistory_IsEmpty()
    {
        var sut = MakeSut();

        sut.GetHistory().Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_NoFile_StartsEmpty()
    {
        var sut = MakeSut();

        await sut.LoadAsync(default);

        sut.GetHistory().Should().BeEmpty();
    }

    [Fact]
    public async Task Add_ThenRestart_HistoryIsRestored()
    {
        var path = Path.Combine(_tmp.Path, "history.json");

        var first = new JsonSearchHistoryService(path, NullLogger<JsonSearchHistoryService>.Instance);
        first.Add("pdf");
        first.Add("doc");
        // Allow fire-and-forget save to complete.
        await Task.Delay(200);

        var second = new JsonSearchHistoryService(path, NullLogger<JsonSearchHistoryService>.Instance);
        await second.LoadAsync(default);

        second.GetHistory().Should().Equal(["doc", "pdf"]);
    }

    [Fact]
    public async Task LoadAsync_TruncatesAtMaxItems()
    {
        // Manually write a file with more items than maxItems.
        var path = Path.Combine(_tmp.Path, "history.json");
        var many = Enumerable.Range(0, 20).Select(i => $"q{i}").ToList();
        await File.WriteAllTextAsync(path,
            System.Text.Json.JsonSerializer.Serialize(many));

        var sut = new JsonSearchHistoryService(path, NullLogger<JsonSearchHistoryService>.Instance, maxItems: 5);
        await sut.LoadAsync(default);

        sut.GetHistory().Should().HaveCount(5);
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_StartsEmpty_DoesNotThrow()
    {
        var path = Path.Combine(_tmp.Path, "history.json");
        await File.WriteAllTextAsync(path, "{{ not json");

        var sut = new JsonSearchHistoryService(path, NullLogger<JsonSearchHistoryService>.Instance);

        var act = async () => await sut.LoadAsync(default);

        await act.Should().NotThrowAsync();
        sut.GetHistory().Should().BeEmpty();
    }

    [Fact]
    public async Task Clear_PersistsEmptyFile()
    {
        var path = Path.Combine(_tmp.Path, "history.json");
        var sut = new JsonSearchHistoryService(path, NullLogger<JsonSearchHistoryService>.Instance);
        sut.Add("x");
        await Task.Delay(200);

        sut.Clear();
        await Task.Delay(200);

        var reload = new JsonSearchHistoryService(path, NullLogger<JsonSearchHistoryService>.Instance);
        await reload.LoadAsync(default);
        reload.GetHistory().Should().BeEmpty();
    }

    [Fact]
    public async Task Remove_PersistsMutation()
    {
        var path = Path.Combine(_tmp.Path, "history.json");
        var sut = new JsonSearchHistoryService(path, NullLogger<JsonSearchHistoryService>.Instance);
        sut.Add("alpha");
        sut.Add("beta");
        await Task.Delay(200);

        sut.Remove("alpha");
        await Task.Delay(200);

        var reload = new JsonSearchHistoryService(path, NullLogger<JsonSearchHistoryService>.Instance);
        await reload.LoadAsync(default);
        reload.GetHistory().Should().Equal(["beta"]);
    }

    [Fact]
    public void Add_NullArg_Throws()
    {
        var sut = MakeSut();

        var act = () => sut.Add(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Remove_NullArg_Throws()
    {
        var sut = MakeSut();

        var act = () => sut.Remove(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ZeroMaxItems_Throws()
    {
        var act = () => new JsonSearchHistoryService(
            Path.Combine(_tmp.Path, "h.json"),
            NullLogger<JsonSearchHistoryService>.Instance,
            maxItems: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Add_ExceedsMax_OldestDroppedAfterReload()
    {
        var path = Path.Combine(_tmp.Path, "history.json");
        var sut = new JsonSearchHistoryService(path, NullLogger<JsonSearchHistoryService>.Instance, maxItems: 3);
        sut.Add("a");
        sut.Add("b");
        sut.Add("c");
        sut.Add("d");   // "a" should be dropped
        await Task.Delay(300);

        var reload = new JsonSearchHistoryService(path, NullLogger<JsonSearchHistoryService>.Instance, maxItems: 3);
        await reload.LoadAsync(default);

        reload.GetHistory().Should().Equal(["d", "c", "b"]);
    }
}
