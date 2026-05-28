using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

public sealed partial class DocumentTabViewModel
{
    /// <summary>Все закладки документа, отсортированные по PageIndex. Биндится в sidebar.</summary>
    public ObservableCollection<Bookmark> Bookmarks { get; } = [];

    /// <summary>Число закладок в документе.</summary>
    public int BookmarksCount => Bookmarks.Count;

    /// <summary>Видимость sidebar'а с закладками (toggle из меню/тулбара). По умолчанию скрыт,
    /// чтобы не отжимать пространство страницы; пользователь включает через View → Bookmarks.</summary>
    [ObservableProperty]
    private bool _isBookmarksVisible;

    [RelayCommand]
    private void ToggleBookmarks() => IsBookmarksVisible = !IsBookmarksVisible;

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

    /// <summary>Удалить указанную закладку из sidecar'а и убрать из <see cref="Bookmarks"/>.
    /// No-op если bookmark null или такого Id уже нет. Сбой логируется, не роняет вкладку.</summary>
    [RelayCommand]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Bookmark removal failure must not crash the tab.")]
    private async Task RemoveBookmarkAsync(Bookmark? bookmark)
    {
        if (bookmark is null)
        {
            return;
        }

        try
        {
            bool removed = await _bookmarkService.RemoveAsync(_filePath, bookmark.Id, CancellationToken.None).ConfigureAwait(false);
            if (!removed)
            {
                return;
            }

            for (int i = Bookmarks.Count - 1; i >= 0; i--)
            {
                if (Bookmarks[i].Id == bookmark.Id)
                {
                    Bookmarks.RemoveAt(i);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove bookmark {Id} from '{Path}'.", bookmark.Id, _filePath);
        }
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

    /// <summary>true когда экспорт закладок применим: каталог форматов известен и есть, что
    /// экспортировать. Биндится к доступности пункта меню «Export bookmarks».</summary>
    public bool CanExportBookmarks => _bookmarkFormats is not null && Bookmarks.Count > 0;

    /// <summary>true когда импорт закладок применим: каталог форматов известен. Импорт работает
    /// и для пустого документа — это его основной сценарий.</summary>
    public bool CanImportBookmarks => _bookmarkFormats is not null;

    /// <summary>true когда импорт PDF /Outlines применим: reader известен и текущий файл — PDF.
    /// EPUB/DjVu/изображения не имеют /Outlines в PDF-смысле.</summary>
    public bool CanImportPdfOutline =>
        _pdfOutlineReader is not null
        && Path.GetExtension(_filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Экспорт всех закладок в файл; формат выбирается по расширению targetPath.
    /// Путь приходит из SaveFileDialog (View). No-op при пустом пути / отсутствующем каталоге
    /// / неизвестном расширении. Сбой логируется, вкладка не падает.</summary>
    [RelayCommand(CanExecute = nameof(CanExportBookmarks))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Bookmark export failure must not crash the tab.")]
    private async Task ExportBookmarksAsync(string? targetPath, CancellationToken ct)
    {
        if (_bookmarkFormats is null || string.IsNullOrWhiteSpace(targetPath))
        {
            return;
        }

        IBookmarkExporter? exporter = _bookmarkFormats.ResolveExporter(targetPath);
        if (exporter is null)
        {
            _logger.LogWarning("No bookmark exporter for '{Path}' (unknown extension).", targetPath);
            return;
        }

        try
        {
            string text = exporter.Export([.. Bookmarks]);
            await File.WriteAllTextAsync(targetPath, text, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // отменено пользователем/закрытием
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to export bookmarks to '{Path}'.", targetPath);
        }
    }

    /// <summary>Импортировать закладки из файла; формат — по расширению sourcePath.
    /// Каждая импортированная запись добавляется через сервис (получает новый Id; перси-
    /// стируется в sidecar). Список в UI обновляется в порядке возрастания PageIndex.
    /// Дубликаты НЕ дедупятся — это решение наслоения, юзер хочет видеть всё.</summary>
    [RelayCommand(CanExecute = nameof(CanImportBookmarks))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Bookmark import failure must not crash the tab.")]
    private async Task ImportBookmarksAsync(string? sourcePath, CancellationToken ct)
    {
        if (_bookmarkFormats is null || string.IsNullOrWhiteSpace(sourcePath))
        {
            return;
        }

        IBookmarkImporter? importer = _bookmarkFormats.ResolveImporter(sourcePath);
        if (importer is null)
        {
            _logger.LogWarning("No bookmark importer for '{Path}' (unknown extension).", sourcePath);
            return;
        }

        try
        {
            string text = await File.ReadAllTextAsync(sourcePath, ct).ConfigureAwait(false);
            var imported = importer.Import(text);
            foreach (var bm in imported)
            {
                ct.ThrowIfCancellationRequested();
                // Сервис генерирует свежий Id и привязывает к fingerprint текущего документа —
                // импортированный Id из формата обмена намеренно отбрасывается (контракт IBookmarkImporter).
                await _bookmarkService.AddAsync(_filePath, bm.PageIndex, bm.Label, ct).ConfigureAwait(false);
            }

            await LoadBookmarksAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // отменено пользователем/закрытием
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to import bookmarks from '{Path}'.", sourcePath);
        }
    }

    /// <summary>Прочитать PDF /Outlines (содержание) и добавить каждый узел как пользовательскую
    /// закладку. Идентичные дубли (page+label) с уже существующими закладками НЕ дедупятся —
    /// пользователь видит точное содержимое импорта и сам решает, что удалить. Сбой логируется
    /// и не роняет вкладку; отмена при shutdown молчаливая.</summary>
    [RelayCommand(CanExecute = nameof(CanImportPdfOutline))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Outline-import failure must not crash the tab.")]
    private async Task ImportPdfOutlineAsync(CancellationToken ct)
    {
        if (_pdfOutlineReader is null)
        {
            return;
        }

        try
        {
            var entries = await _pdfOutlineReader.ReadAsync(_filePath, ct).ConfigureAwait(false);
            if (entries.Count == 0)
            {
                _logger.LogInformation("PDF '{Path}' has no outline; nothing to import.", _filePath);
                return;
            }

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                // Depth прокидываем как есть — Markdown-экспорт отрисует indent, sidebar (когда
                // появится) — иерархический отступ.
                string label = string.IsNullOrWhiteSpace(entry.Title) ? $"Page {entry.PageIndex + 1}" : entry.Title;
                await _bookmarkService.AddAsync(_filePath, entry.PageIndex, label, ct, depth: entry.Depth).ConfigureAwait(false);
            }

            await LoadBookmarksAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // shutdown / пользователь отменил
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to import PDF outline from '{Path}'.", _filePath);
        }
    }
}

/// <summary>Запрос переименования закладки, передаваемый в
/// <see cref="DocumentTabViewModel.RenameBookmarkCommand"/>.</summary>
public sealed record RenameBookmarkRequest(Bookmark Bookmark, string NewLabel);
