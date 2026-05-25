namespace Foliant.Domain;

/// <summary>
/// Контракт редактирования документа. Реализация — через command-pattern + event store
/// (Auto-save и Undo/Redo бесплатно). См. IMPLEMENTATION_PLAN.md, раздел 5.5.
/// </summary>
public interface IDocumentEditor
{
    bool IsDirty { get; }

    Task ApplyAsync(IDocumentCommand command, CancellationToken ct);

    Task UndoAsync(CancellationToken ct);

    Task RedoAsync(CancellationToken ct);

    Task SaveAsync(string? path, CancellationToken ct);
}

/// <summary>
/// Сериализуемое <b>намерение</b> редактирования (intent), а не действие над живым
/// документом. Редактор сам решает, как применить запись к байтам PDF, и хранит её
/// в event store для undo/redo (snapshot + replay) и crash recovery. Поэтому здесь
/// нет ссылки на <see cref="IDocument"/> и нет обратной операции — отмена реализуется
/// повторным проигрыванием журнала от исходного снимка.
/// </summary>
public interface IDocumentCommand
{
    /// <summary>Дискриминатор для журнала событий (event store): сопоставляется с
    /// конкретным обработчиком при replay. Стабилен между версиями.</summary>
    string Kind { get; }

    /// <summary>Сериализует намерение в wire-формат event store (Kind + JSON-payload).</summary>
    DocumentCommandRecord ToRecord();
}
