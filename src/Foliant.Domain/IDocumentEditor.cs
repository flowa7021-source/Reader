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

public interface IDocumentCommand
{
    /// <summary>Стабильный ID для журнала событий (event store).</summary>
    string Id { get; }

    Task ApplyAsync(IDocument document, CancellationToken ct);

    Task InvertAsync(IDocument document, CancellationToken ct);
}
