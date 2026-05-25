using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.Input;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

public sealed partial class DocumentTabViewModel
{
    /// <summary>Все закладки документа, отсортированные по PageIndex. Биндится в sidebar.</summary>
    public ObservableCollection<Bookmark> Bookmarks { get; } = [];

    /// <summary>Число закладок в документе.</summary>
    public int BookmarksCount => Bookmarks.Count;

    /// <summary><c>true</c> if the current page has at least one bookmark. Updates when
    /// <see cref="CurrentPageIndex"/> changes or when <see cref="Bookmarks"/> changes.</summary>
    public bool IsCurrentPageBookmarked =>
        Bookmarks.Any(b => b.PageIndex == CurrentPageIndex);

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Bookmark load failure must not crash the tab.")]
    public async Task LoadBookmarksAsync(CancellationToken ct)
    {
        try
        {
            var loaded = await _bookmarkService.ListAsync(_filePath, ct);
            Bookmarks.Clear();
            foreach (var bm in loaded.OrderBy(b => b.PageIndex))
            {
                Bookmarks.Add(bm);
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown — игнорируем
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load bookmarks for '{Path}'.", _filePath);
        }
    }

    /// <summary>Toggle закладки на текущей странице. Label = "Page N" по умолчанию.</summary>
    [RelayCommand]
    private async Task ToggleBookmarkAsync()
    {
        int page = CurrentPageIndex;
        string defaultLabel = $"Page {page + 1}";

        var bm = await _bookmarkService.ToggleAsync(_filePath, page, defaultLabel, CancellationToken.None);
        if (bm is null)
        {
            // удалили — выкидываем по PageIndex (на странице была одна закладка по контракту Toggle).
            for (int i = Bookmarks.Count - 1; i >= 0; i--)
            {
                if (Bookmarks[i].PageIndex == page)
                {
                    Bookmarks.RemoveAt(i);
                }
            }
            return;
        }

        // вставляем сохранив сортировку по PageIndex
        int insertAt = 0;
        while (insertAt < Bookmarks.Count && Bookmarks[insertAt].PageIndex < bm.PageIndex)
        {
            insertAt++;
        }
        Bookmarks.Insert(insertAt, bm);
    }

    /// <summary>Переименовать закладку. <paramref name="request"/> содержит оригинальную
    /// закладку и новый текст метки. Пустой/null запрос или пустая метка — no-op.</summary>
    [RelayCommand]
    private async Task RenameBookmarkAsync(RenameBookmarkRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.NewLabel))
        {
            return;
        }

        var updated = await _bookmarkService.RenameAsync(
            _filePath, request.Bookmark.Id, request.NewLabel.Trim(), CancellationToken.None);

        if (updated is null)
        {
            return;
        }

        for (int i = 0; i < Bookmarks.Count; i++)
        {
            if (Bookmarks[i].Id == updated.Id)
            {
                Bookmarks[i] = updated;
                break;
            }
        }
    }

    [RelayCommand]
    private void JumpToBookmark(Bookmark? bookmark)
    {
        if (bookmark is null)
        {
            return;
        }
        CurrentPageIndex = bookmark.PageIndex;
    }

    /// <summary>Прыгает на ближайшую закладку с PageIndex &gt; CurrentPageIndex; wrap к первой.</summary>
    [RelayCommand]
    private void NextBookmark()
    {
        if (Bookmarks.Count == 0)
        {
            return;
        }
        Bookmark? target = Bookmarks.FirstOrDefault(b => b.PageIndex > CurrentPageIndex);
        target ??= Bookmarks[0];
        CurrentPageIndex = target.PageIndex;
    }

    /// <summary>Прыгает на ближайшую закладку с PageIndex &lt; CurrentPageIndex; wrap к последней.</summary>
    [RelayCommand]
    private void PreviousBookmark()
    {
        if (Bookmarks.Count == 0)
        {
            return;
        }
        Bookmark? target = Bookmarks.LastOrDefault(b => b.PageIndex < CurrentPageIndex);
        target ??= Bookmarks[^1];
        CurrentPageIndex = target.PageIndex;
    }
}

/// <summary>Запрос переименования закладки, передаваемый в
/// <see cref="DocumentTabViewModel.RenameBookmarkCommand"/>.</summary>
public sealed record RenameBookmarkRequest(Bookmark Bookmark, string NewLabel);
