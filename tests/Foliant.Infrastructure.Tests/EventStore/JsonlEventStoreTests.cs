using FluentAssertions;
using Foliant.Domain;
using Foliant.Infrastructure.EventStore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Infrastructure.Tests.EventStore;

public sealed class JsonlEventStoreTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private readonly JsonlEventStore _sut;
    private const string Fp = "doc-fp-evt";

    public JsonlEventStoreTests()
    {
        _sut = new JsonlEventStore(_tmp.Path, NullLogger<JsonlEventStore>.Instance);
    }

    public void Dispose()
    {
        _sut.Dispose();
        _tmp.Dispose();
    }

    [Fact]
    public async Task ReadAll_NoFile_YieldsNothing()
    {
        var collected = new List<DocumentCommandRecord>();
        await foreach (var rec in _sut.ReadAllAsync(Fp, default))
        {
            collected.Add(rec);
        }
        collected.Should().BeEmpty();
    }

    [Fact]
    public async Task AppendThenReadAll_RoundtripsInOrder()
    {
        await _sut.AppendAsync(Fp, new DocumentCommandRecord("InsertPage", """{"at":0}"""), default);
        await _sut.AppendAsync(Fp, new DocumentCommandRecord("RotatePage", """{"page":2,"by":90}"""), default);
        await _sut.AppendAsync(Fp, new DocumentCommandRecord("DeletePage", """{"page":4}"""), default);

        var collected = new List<DocumentCommandRecord>();
        await foreach (var rec in _sut.ReadAllAsync(Fp, default))
        {
            collected.Add(rec);
        }

        collected.Select(r => r.Kind).Should().Equal(["InsertPage", "RotatePage", "DeletePage"]);
        collected[1].PayloadJson.Should().Be("""{"page":2,"by":90}""");
    }

    [Fact]
    public async Task Append_PayloadWithCyrillic_RoundtripsLossless()
    {
        var rec = new DocumentCommandRecord("AddNote", """{"text":"Привет — мир"}""");

        await _sut.AppendAsync(Fp, rec, default);

        var read = new List<DocumentCommandRecord>();
        await foreach (var r in _sut.ReadAllAsync(Fp, default))
        {
            read.Add(r);
        }

        read.Should().ContainSingle();
        read[0].PayloadJson.Should().Contain("Привет");
    }

    [Fact]
    public async Task Clear_DropsEntireDocFolder()
    {
        await _sut.AppendAsync(Fp, new DocumentCommandRecord("X", "{}"), default);
        await _sut.ClearAsync(Fp, default);

        var collected = new List<DocumentCommandRecord>();
        await foreach (var r in _sut.ReadAllAsync(Fp, default))
        {
            collected.Add(r);
        }
        collected.Should().BeEmpty();
    }

    [Fact]
    public async Task DifferentDocuments_AreIsolated()
    {
        await _sut.AppendAsync("doc-A", new DocumentCommandRecord("A", "{}"), default);
        await _sut.AppendAsync("doc-B", new DocumentCommandRecord("B", "{}"), default);

        var aRecs = new List<DocumentCommandRecord>();
        await foreach (var r in _sut.ReadAllAsync("doc-A", default))
        {
            aRecs.Add(r);
        }
        aRecs.Should().ContainSingle().Which.Kind.Should().Be("A");
    }

    [Fact]
    public async Task ReadAll_CorruptLine_IsSkippedNotPropagated()
    {
        // подкладываем хорошую запись + битую строку + хорошую — replay должен отдать только хорошие
        var docDir = Path.Combine(_tmp.Path, Fp);
        Directory.CreateDirectory(docDir);
        var path = Path.Combine(docDir, "events.jsonl");
        await File.WriteAllTextAsync(
            path,
            """
            {"Kind":"Good1","PayloadJson":"{}"}
            {{ this is not json
            {"Kind":"Good2","PayloadJson":"{}"}

            """, default);

        var collected = new List<DocumentCommandRecord>();
        await foreach (var r in _sut.ReadAllAsync(Fp, default))
        {
            collected.Add(r);
        }

        collected.Select(r => r.Kind).Should().Equal(["Good1", "Good2"]);
    }

    [Fact]
    public async Task ConcurrentAppends_SameDoc_AllSurvive()
    {
        var tasks = Enumerable.Range(0, 30).Select(i =>
            _sut.AppendAsync(Fp, new DocumentCommandRecord($"Cmd{i}", "{}"), default)).ToArray();

        await Task.WhenAll(tasks);

        var collected = new List<DocumentCommandRecord>();
        await foreach (var r in _sut.ReadAllAsync(Fp, default))
        {
            collected.Add(r);
        }
        collected.Should().HaveCount(30);
        collected.Select(r => r.Kind).Should().BeEquivalentTo(Enumerable.Range(0, 30).Select(i => $"Cmd{i}"));
    }

    [Fact]
    public async Task Survives_Restart()
    {
        await _sut.AppendAsync(Fp, new DocumentCommandRecord("Persisted", "{}"), default);
        _sut.Dispose();

        using var fresh = new JsonlEventStore(_tmp.Path, NullLogger<JsonlEventStore>.Instance);
        var collected = new List<DocumentCommandRecord>();
        await foreach (var r in fresh.ReadAllAsync(Fp, default))
        {
            collected.Add(r);
        }
        collected.Should().ContainSingle().Which.Kind.Should().Be("Persisted");
    }
}
