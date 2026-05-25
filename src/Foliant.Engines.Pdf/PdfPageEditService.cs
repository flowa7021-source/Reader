using Foliant.Application.Services;
using Foliant.Domain;
using Foliant.Engines.Pdf.Editing;

namespace Foliant.Engines.Pdf;

/// <summary>
/// PDF-реализация <see cref="IPageEditService"/>: транслирует высокоуровневые операции
/// VM в engine-команды (<see cref="RotatePageCommand"/> / <see cref="DeletePageCommand"/>),
/// применяет их через <see cref="IDocumentEditor"/> и сохраняет. Команды живут в этом
/// движке, поэтому VM-слой их не видит.
/// </summary>
public sealed class PdfPageEditService : IPageEditService
{
    public bool CanEdit(IDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.GetEditor() is not null;
    }

    public async Task RotatePageAsync(IDocument document, int pageIndex, ViewRotation rotation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        IDocumentEditor editor = GetEditor(document);
        await editor.ApplyAsync(new RotatePageCommand(pageIndex, rotation), ct).ConfigureAwait(false);
        await editor.SaveAsync(null, ct).ConfigureAwait(false);
    }

    public async Task DeletePageAsync(IDocument document, int pageIndex, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        IDocumentEditor editor = GetEditor(document);
        await editor.ApplyAsync(new DeletePageCommand(pageIndex), ct).ConfigureAwait(false);
        await editor.SaveAsync(null, ct).ConfigureAwait(false);
    }

    public async Task ReorderPagesAsync(IDocument document, IReadOnlyList<int> newOrder, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(newOrder);
        IDocumentEditor editor = GetEditor(document);
        await editor.ApplyAsync(new ReorderPagesCommand(newOrder), ct).ConfigureAwait(false);
        await editor.SaveAsync(null, ct).ConfigureAwait(false);
    }

    private static IDocumentEditor GetEditor(IDocument document) =>
        document.GetEditor() ?? throw new InvalidOperationException("Document is read-only; no editor available.");
}
