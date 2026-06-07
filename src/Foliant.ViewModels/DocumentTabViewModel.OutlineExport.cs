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
    /// (с richness-опциями из <paramref name="request"/>) и записать их в /Outlines исходного PDF, сохранив
    /// результат в <see cref="ExportOutlineRequest.TargetPath"/>. Порядок и <see cref="Bookmark.Depth"/>
    /// сохраняются — writer строит дерево из глубины, так что nested-закладки переживают round-trip.
    /// No-op при отсутствии writer'а, не-PDF источнике или пустом пути. Сбой логируется и не роняет
    /// вкладку — как соседние PDF-mutate команды.</summary>
    [RelayCommand(CanExecute = nameof(CanExportBookmarksToPdf))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Outline-export failure must not crash the tab.")]
    private async Task ExportBookmarksToPdfAsync(ExportOutlineRequest? request, CancellationToken ct)
    {
        if (_outlineWriter is null
            || request is null
            || string.IsNullOrWhiteSpace(request.TargetPath)
            || !CanExportBookmarksToPdf)
        {
            return;
        }

        // Сохраняем порядок sidecar'а (он отсортирован по PageIndex) и Depth — writer уже умеет
        // зажимать out-of-range PageIndex и строить иерархию из глубины (см. IPdfOutlineWriter).
        // Richness применяется единообразно: режим перехода — ко всем узлам; «collapse» сворачивает
        // узлы с детьми (IsOpen=false → отрицательный /Count); «bold top-level» — жирный только корням.
        IReadOnlyList<DocumentOutlineEntry> entries =
        [
            .. Bookmarks.Select(b => new DocumentOutlineEntry(
                b.PageIndex,
                b.Label,
                b.Depth,
                Destination: request.Destination,
                IsBold: request.BoldTopLevel && b.Depth == 0,
                IsOpen: !request.CollapseNested)),
        ];

        try
        {
            await _outlineWriter.WriteOutlineAsync(_filePath, request.TargetPath, entries, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // отменено пользователем/закрытием
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to export bookmarks to PDF outline '{Path}'.", request.TargetPath);
        }
    }
}

/// <summary>View-supplied envelope for <c>ExportBookmarksToPdfCommand</c>: the Save-As target plus the
/// outline richness options gathered from the export dialog. Defined here (not in Domain) because it is
/// a UI-flow concern, mirroring the other <c>*Request</c> records.</summary>
/// <param name="TargetPath">Save-As target chosen by the caller.</param>
/// <param name="Destination">Destination zoom mode applied to every generated node.</param>
/// <param name="CollapseNested">When <see langword="true"/>, parent nodes are written collapsed
/// (negative <c>/Count</c>) so the outline opens compact.</param>
/// <param name="BoldTopLevel">When <see langword="true"/>, top-level (depth-0) entries are bold.</param>
public sealed record ExportOutlineRequest(
    string TargetPath,
    OutlineDestinationMode Destination = OutlineDestinationMode.FitPage,
    bool CollapseNested = false,
    bool BoldTopLevel = false);
