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

    /// <summary>Список fingerprint'ов, у которых есть непустой <c>events.jsonl</c>.
    /// Используется при старте приложения для crash recovery: «у вас есть несохранённые
    /// действия для документов X, Y, Z». Пустые / отсутствующие файлы пропускаются.</summary>
    Task<IReadOnlyList<string>> ListPendingFingerprintsAsync(CancellationToken ct);

    /// <summary>Сколько событий в <c>events.jsonl</c> у документа. 0 для отсутствующего файла.
    /// Подсчитывает только non-blank строки — JSON содержимое не парсится. Дёшево
    /// относительно <see cref="ReadAllAsync"/>: достаточно для UI-индикатора
    /// «12 unsaved actions» в crash-recovery диалоге.</summary>
    Task<int> GetEventCountAsync(string docFingerprint, CancellationToken ct);
}
