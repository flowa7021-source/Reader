using Foliant.Application.Services;
using Foliant.Domain;

namespace Foliant.Engines.Pdf.Editing;

/// <summary>
/// Event-sourced редактор PDF. Undo реализован через snapshot+replay (а не обратную
/// команду): хранит неизменяемый снимок <c>_base</c> (байты файла на момент открытия)
/// и журнал применённых записей; отмена = заново проиграть журнал от снимка. Это
/// надёжно для бинарных PDF, где точную обратную операцию построить тяжело.
///
/// Каждое применение/повтор также пишется в <see cref="IEventStore"/> (ключ —
/// fingerprint файла) для crash recovery. Конструктор НЕ проигрывает прошлые события:
/// восстановление после сбоя — отдельный VM-поток, читающий store напрямую.
/// </summary>
public sealed class PdfDocumentEditor : IDocumentEditor
{
    private readonly string _fingerprint;
    private readonly IEventStore _eventStore;
    private readonly string _originalPath;
    private readonly Func<byte[], DocumentCommandRecord, byte[]> _dispatch;
    private readonly List<DocumentCommandRecord> _applied = [];
    private readonly Stack<DocumentCommandRecord> _redo = new();

    private byte[] _base;
    private byte[] _working;

    public PdfDocumentEditor(
        byte[] baseBytes,
        string fingerprint,
        IEventStore eventStore,
        string originalPath,
        Func<byte[], DocumentCommandRecord, byte[]>? dispatch = null)
    {
        ArgumentNullException.ThrowIfNull(baseBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);

        _base = baseBytes;
        _working = baseBytes;
        _fingerprint = fingerprint;
        _eventStore = eventStore;
        _originalPath = originalPath;
        _dispatch = dispatch ?? PdfCommandDispatcher.Dispatch;
    }

    public bool IsDirty { get; private set; }

    public async Task ApplyAsync(IDocumentCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var record = command.ToRecord();
        byte[] next = await RunDispatchAsync(_working, record, ct).ConfigureAwait(false);

        await _eventStore.AppendAsync(_fingerprint, record, ct).ConfigureAwait(false);
        _working = next;
        _applied.Add(record);
        _redo.Clear();
        IsDirty = true;
    }

    public async Task UndoAsync(CancellationToken ct)
    {
        if (_applied.Count == 0)
        {
            return;
        }

        var last = _applied[^1];
        _applied.RemoveAt(_applied.Count - 1);
        _redo.Push(last);

        _working = await ReplayAsync(_base, _applied, ct).ConfigureAwait(false);
        await _eventStore.CompactAsync(_fingerprint, _applied, ct).ConfigureAwait(false);
        IsDirty = true;
    }

    public async Task RedoAsync(CancellationToken ct)
    {
        if (_redo.Count == 0)
        {
            return;
        }

        var record = _redo.Pop();
        byte[] next = await RunDispatchAsync(_working, record, ct).ConfigureAwait(false);

        await _eventStore.AppendAsync(_fingerprint, record, ct).ConfigureAwait(false);
        _working = next;
        _applied.Add(record);
        IsDirty = true;
    }

    public async Task SaveAsync(string? path, CancellationToken ct)
    {
        string target = path ?? _originalPath;
        byte[] snapshot = _working;

        // FRAGILE: atomic save — temp в той же папке + File.Move(overwrite). До успешного
        // Move исходный файл не трогаем; ошибка/отмена не должна оставить полу-файл.
        await WriteAtomicAsync(target, snapshot, ct).ConfigureAwait(false);

        _base = snapshot;
        _applied.Clear();
        _redo.Clear();
        await _eventStore.CompactAsync(_fingerprint, [], ct).ConfigureAwait(false);
        IsDirty = false;
    }

    private Task<byte[]> RunDispatchAsync(byte[] input, DocumentCommandRecord record, CancellationToken ct) =>
        Task.Run(() => _dispatch(input, record), ct);

    private Task<byte[]> ReplayAsync(byte[] from, IReadOnlyList<DocumentCommandRecord> records, CancellationToken ct) =>
        Task.Run(() => Replay(from, records), ct);

    private byte[] Replay(byte[] from, IReadOnlyList<DocumentCommandRecord> records)
    {
        // FRAGILE: replay boundary — fold dispatch over the immutable snapshot.
        byte[] acc = from;
        foreach (var record in records)
        {
            acc = _dispatch(acc, record);
        }
        return acc;
    }

    private static async Task WriteAtomicAsync(string target, byte[] bytes, CancellationToken ct)
    {
        // FRAGILE: PDF-mutation/IO boundary — данные пишутся на диск, риск потери при сбое.
        string dir = Path.GetDirectoryName(Path.GetFullPath(target))!;
        Directory.CreateDirectory(dir);
        string tmp = Path.Combine(dir, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(tmp, bytes, ct).ConfigureAwait(false);
            File.Move(tmp, target, overwrite: true);
        }
        finally
        {
            TryDelete(tmp);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // best-effort cleanup of the temp file; nothing actionable on failure.
        }
        catch (UnauthorizedAccessException)
        {
            // best-effort cleanup of the temp file; nothing actionable on failure.
        }
    }
}
