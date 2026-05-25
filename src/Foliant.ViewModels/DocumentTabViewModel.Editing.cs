using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.Input;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

public sealed partial class DocumentTabViewModel
{
    /// <summary>True если страницы документа можно редактировать (есть редактор + reopen).</summary>
    public bool CanEditPages =>
        _pageEdit is not null && _openUseCase is not null && _pageEdit.CanEdit(_document);

    /// <summary>Удалять можно, пока остаётся больше одной страницы.</summary>
    public bool CanDeleteCurrentPage => CanEditPages && PageCount > 1;

    /// <summary>Текущую страницу можно поднять, если она не первая.</summary>
    public bool CanMovePageUp => CanEditPages && CurrentPageIndex > 0;

    /// <summary>Текущую страницу можно опустить, если она не последняя.</summary>
    public bool CanMovePageDown => CanEditPages && CurrentPageIndex < PageCount - 1;

    [RelayCommand(CanExecute = nameof(CanEditPages))]
    private Task RotateCurrentPageAsync() =>
        EditAndReloadAsync(d => _pageEdit!.RotatePageAsync(d, CurrentPageIndex, ViewRotation.Cw90, CancellationToken.None));

    [RelayCommand(CanExecute = nameof(CanDeleteCurrentPage))]
    private Task DeleteCurrentPageAsync() =>
        EditAndReloadAsync(d => _pageEdit!.DeletePageAsync(d, CurrentPageIndex, CancellationToken.None));

    [RelayCommand(CanExecute = nameof(CanMovePageUp))]
    private Task MovePageUpAsync() => MovePageAsync(CurrentPageIndex, CurrentPageIndex - 1);

    [RelayCommand(CanExecute = nameof(CanMovePageDown))]
    private Task MovePageDownAsync() => MovePageAsync(CurrentPageIndex, CurrentPageIndex + 1);

    private Task MovePageAsync(int from, int to)
    {
        if (from == to || to < 0 || to >= PageCount)
        {
            return Task.CompletedTask;
        }

        int[] order = BuildSwapOrder(PageCount, from, to);
        return EditAndReloadAsync(
            d => _pageEdit!.ReorderPagesAsync(d, order, CancellationToken.None),
            afterReload: () => CurrentPageIndex = to);
    }

    private static int[] BuildSwapOrder(int count, int a, int b)
    {
        var order = new int[count];
        for (int i = 0; i < count; i++)
        {
            order[i] = i;
        }

        (order[a], order[b]) = (order[b], order[a]);
        return order;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Page-edit failure must surface as an error message, not crash the tab.")]
    private async Task EditAndReloadAsync(Func<IDocument, Task> edit, Action? afterReload = null)
    {
        if (_pageEdit is null || _openUseCase is null)
        {
            return;
        }

        try
        {
            await edit(_document);
            await ReloadAsync(CancellationToken.None);
            afterReload?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Page edit failed for '{Path}'.", _filePath);
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>
    /// Переоткрыть документ с диска после правки структуры (rotate/delete/…): редактор
    /// сохраняет новый PDF, но открытый <see cref="IDocument"/> держит старый рендер-хэндл —
    /// поэтому здесь мы загружаем свежий документ, диспозим старый и перерисовываем.
    /// </summary>
    public async Task ReloadAsync(CancellationToken ct)
    {
        if (_openUseCase is null)
        {
            return;
        }

        IDocument fresh = await _openUseCase.ExecuteAsync(_filePath, ct);
        IDocument old = _document;
        _document = fresh;
        PageCount = fresh.PageCount;
        CurrentPageIndex = Math.Clamp(CurrentPageIndex, 0, Math.Max(0, PageCount - 1));

        await old.DisposeAsync();
        await RenderCurrentPageAsync(ct);
        await LoadAnnotationsAsync(ct);
        await LoadBookmarksAsync(ct);
    }
}
