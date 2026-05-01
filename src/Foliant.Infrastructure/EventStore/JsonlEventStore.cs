using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.Infrastructure.EventStore;

/// <summary>
/// JSONL-реализация <see cref="IEventStore"/>: каждая команда — отдельная JSON
/// строка в <c>{rootDir}/{fingerprint}/events.jsonl</c>. Append-only — никогда
/// не редактируем существующие строки. Per-document <see cref="SemaphoreSlim"/>
/// сериализует concurrent appends для одного документа.
/// </summary>
public sealed class JsonlEventStore : IEventStore, IDisposable
{
    private readonly string _rootDir;
    private readonly ILogger<JsonlEventStore> _log;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    public JsonlEventStore(string rootDir, ILogger<JsonlEventStore> log)
    {
        ArgumentNullException.ThrowIfNull(rootDir);
        ArgumentNullException.ThrowIfNull(log);
        _rootDir = rootDir;
        _log = log;
        Directory.CreateDirectory(_rootDir);
    }

    public async Task AppendAsync(string docFingerprint, DocumentCommandRecord record, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docFingerprint);
        ArgumentNullException.ThrowIfNull(record);

        var gate = GetGate(docFingerprint);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var path = StreamPath(docFingerprint);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var line = JsonSerializer.Serialize(record, EventStoreJsonContext.Default.DocumentCommandRecord);
            await File.AppendAllTextAsync(path, line + "\n", Encoding.UTF8, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Battered jsonl line must not crash replay; we log and skip.")]
    public async IAsyncEnumerable<DocumentCommandRecord> ReadAllAsync(
        string docFingerprint,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docFingerprint);

        var path = StreamPath(docFingerprint);
        if (!File.Exists(path))
        {
            yield break;
        }

        await using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            DocumentCommandRecord? rec = null;
            try
            {
                rec = JsonSerializer.Deserialize(line, EventStoreJsonContext.Default.DocumentCommandRecord);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                _log.LogWarning(ex, "Corrupt JSONL line in event store {Path}; skipping", path);
                continue;
            }

            if (rec is not null)
            {
                yield return rec;
            }
        }
    }

    public Task ClearAsync(string docFingerprint, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docFingerprint);

        var dir = DocDir(docFingerprint);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListPendingFingerprintsAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_rootDir))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        var result = new List<string>();
        foreach (var dir in Directory.EnumerateDirectories(_rootDir))
        {
            ct.ThrowIfCancellationRequested();
            var jsonl = Path.Combine(dir, "events.jsonl");
            if (!File.Exists(jsonl))
            {
                continue;
            }
            var info = new FileInfo(jsonl);
            if (info.Length == 0)
            {
                continue;
            }
            result.Add(Path.GetFileName(dir));
        }

        return Task.FromResult<IReadOnlyList<string>>(result);
    }

    public void Dispose()
    {
        foreach (var s in _gates.Values)
        {
            s.Dispose();
        }
        _gates.Clear();
    }

    private SemaphoreSlim GetGate(string fp) =>
        _gates.GetOrAdd(fp, _ => new SemaphoreSlim(1, 1));

    private string DocDir(string fp) => Path.Combine(_rootDir, fp);

    private string StreamPath(string fp) => Path.Combine(DocDir(fp), "events.jsonl");
}

[JsonSerializable(typeof(DocumentCommandRecord))]
internal sealed partial class EventStoreJsonContext : JsonSerializerContext;
