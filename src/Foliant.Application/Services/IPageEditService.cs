using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Высокоуровневые операции над страницами PDF (поворот/удаление) для ViewModel-слоя.
/// Скрывает engine-специфичные <c>IDocumentCommand</c> (они живут в движке) за портом,
/// чтобы VM не зависел от <c>Foliant.Engines.Pdf</c>. Каждая операция применяет команду
/// через <see cref="IDocumentEditor"/> и сохраняет результат на диск; перезагрузку
/// документа в UI выполняет вызывающий (см. reload-after-edit в DocumentTabViewModel).
/// </summary>
public interface IPageEditService
{
    /// <summary>True если документ редактируем (<see cref="IDocument.GetEditor"/> != null).</summary>
    bool CanEdit(IDocument document);

    Task RotatePageAsync(IDocument document, int pageIndex, ViewRotation rotation, CancellationToken ct);

    Task DeletePageAsync(IDocument document, int pageIndex, CancellationToken ct);

    /// <summary>Переупорядочить страницы. <paramref name="newOrder"/> — перестановка
    /// 0-based индексов <c>0..PageCount-1</c> в желаемом порядке.</summary>
    Task ReorderPagesAsync(IDocument document, IReadOnlyList<int> newOrder, CancellationToken ct);
}
