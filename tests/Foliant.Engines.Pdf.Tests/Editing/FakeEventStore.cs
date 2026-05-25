using System.Runtime.CompilerServices;
using Foliant.Application.Services;
using Foliant.Domain;

namespace Foliant.Engines.Pdf.Tests.Editing;

/// <summary>
/// In-memory <see cref="IEventStore"/> для unit-тестов редактора: фиксирует число
/// Append/Compact и хранит текущий «журнал» (последний Compact перезаписывает его).
/// Не пишет на диск — никаких нативных/IO зависимостей.
/// </summary>
internal sealed class FakeEventStore : IEventStore
{
    private readonly Dictionary<string, List<DocumentCommandRecord>> _streams = new(StringComparer.Ordinal);

    public int AppendCount { get; private set; }
    public int CompactCount { get; private set; }

    public IReadOnlyList<DocumentCommandRecord> Stream(string fp) =>
        _streams.TryGetValue(fp, out var list) ? list : [];

    public Task AppendAsync(string docFingerprint, DocumentCommandRecord record, CancellationToken ct)
    {
        AppendCount++;
        Get(docFingerprint).Add(record);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<DocumentCommandRecord> ReadAllAsync(
        string docFingerprint,
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var rec in Stream(docFingerprint))
        {
            ct.ThrowIfCancellationRequested();
            yield return rec;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public Task ClearAsync(string docFingerprint, CancellationToken ct)
    {
        _streams.Remove(docFingerprint);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListPendingFingerprintsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>([.. _streams.Keys]);

    public Task<int> GetEventCountAsync(string docFingerprint, CancellationToken ct) =>
        Task.FromResult(Stream(docFingerprint).Count);

    public Task CompactAsync(
        string docFingerprint,
        IReadOnlyList<DocumentCommandRecord> retained,
        CancellationToken ct)
    {
        CompactCount++;
        _streams[docFingerprint] = [.. retained];
        return Task.CompletedTask;
    }

    private List<DocumentCommandRecord> Get(string fp)
    {
        if (!_streams.TryGetValue(fp, out var list))
        {
            list = [];
            _streams[fp] = list;
        }
        return list;
    }
}
