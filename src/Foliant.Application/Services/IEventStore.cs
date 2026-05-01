using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Append-only event store команд редактора. Persists в JSONL внутри
/// <c>Autosave/{fingerprint}/events.jsonl</c>. Нужен для:
///   - undo/redo (replay-from-зачала с применением Apply / Invert);
///   - crash recovery (при старте сканируем Autosave/* и предлагаем восстановить);
///   - будущей коллаборации (Phase 3).
///
/// Per-document, ключ — <c>docFingerprint</c>. Записи неизменяемые;
/// «отменить» = записать обратную команду.
/// </summary>
public interface IEventStore
{
    Task AppendAsync(string docFingerprint, DocumentCommandRecord record, CancellationToken ct);

    IAsyncEnumerable<DocumentCommandRecord> ReadAllAsync(string docFingerprint, CancellationToken ct);

    Task ClearAsync(string docFingerprint, CancellationToken ct);
}
