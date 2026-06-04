using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

/// <summary>
/// «Export Bookmarks to PDF» — записывает sidecar-закладки пользователя в сам PDF /Outlines
/// (через <see cref="IPdfOutlineWriter"/>), чтобы они стали видны в Acrobat / любом viewer'е.
/// Обратная операция к «Import PDF Outline»: тот читает /Outlines в закладки, этот пишет их
/// назад. View собирает target-путь (Save-As) и форвардит команде — VM пути не угадывает.
/// </summary>
public sealed partial class DocumentTabViewModel
{
    /// <summary>Можно ли записать закладки в PDF /Outlines: writer подключён, источник — PDF,
    /// и есть что экспортировать (пустой /Outlines бессмысленен — у документа уже нет содержания).
    /// Та же форма gate, что у <see cref="CanApplyBates"/>, плюс непустой список закладок.</summary>
    public bool CanExportBookmarksToPdf =>
        _outlineWriter is not null
        && Bookmarks.Count > 0
        && Path.GetExtension(_filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Сконвертировать <see cref="Bookmarks"/> в плоский <see cref="DocumentOutlineEntry"/>-список
    /// и записать их в /Outlines исходного PDF, сохранив результат в <paramref name="targetPath"/>.
    /// Порядок и <see cref="Bookmark.Depth"/> сохраняются — writer строит дерево из глубины, так что
    /// nested-закладки переживают round-trip. No-op при отсутствии writer'а, не-PDF источнике или
    /// пустом пути. Сбой логируется и не роняет вкладку — как соседние PDF-mutate команды.</summary>
    [RelayCommand(CanExecute = nameof(CanExportBookmarksToPdf))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Outline-export failure must not crash the tab.")]
    private async Task ExportBookmarksToPdfAsync(string? targetPath, CancellationToken ct)
    {
        if (_outlineWriter is null
            || string.IsNullOrWhiteSpace(targetPath)
            || !CanExportBookmarksToPdf)
        {
            return;
        }

        // Сохраняем порядок sidecar'а (он отсортирован по PageIndex) и Depth — writer уже умеет
        // зажимать out-of-range PageIndex и строить иерархию из глубины (см. IPdfOutlineWriter).
        IReadOnlyList<DocumentOutlineEntry> entries =
            [.. Bookmarks.Select(b => new DocumentOutlineEntry(b.PageIndex, b.Label, b.Depth))];

        try
        {
            await _outlineWriter.WriteOutlineAsync(_filePath, targetPath, entries, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // отменено пользователем/закрытием
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to export bookmarks to PDF outline '{Path}'.", targetPath);
        }
    }
}
