using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

public sealed partial class DocumentTabViewModel
{
    /// <summary>true когда экспорт текущей страницы в PNG/JPEG применим — экспортер задан в DI
    /// и документ загружен (PageCount > 0). Расширение/формат определяет View по targetPath.</summary>
    public bool CanExportCurrentPageAsImage => _pageImageExporter is not null && PageCount > 0;

    /// <summary>Экспорт <see cref="CurrentPageIndex"/> в bitmap-файл. Zoom берётся из текущего
    /// view-state (<see cref="Zoom"/>) — пользователь получает «то, что видит» по DPI. View
    /// показывает SaveFileDialog и передаёт сюда targetPath (расширение → формат). No-op при
    /// пустом пути / отсутствующем экспортере. Сбой логируется, вкладка не падает.</summary>
    [RelayCommand(CanExecute = nameof(CanExportCurrentPageAsImage))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Page-image export failure must not crash the tab.")]
    private async Task ExportCurrentPageAsImageAsync(string? targetPath, CancellationToken ct)
    {
        if (_pageImageExporter is null || string.IsNullOrWhiteSpace(targetPath))
        {
            return;
        }

        try
        {
            await _pageImageExporter.ExportAsync(_document, CurrentPageIndex, Zoom, targetPath, ct).ConfigureAwait(false);
            _logger.LogInformation("Exported page {Page} of '{Source}' to image '{Target}'.",
                CurrentPageIndex, _filePath, targetPath);
        }
        catch (OperationCanceledException)
        {
            // отменено пользователем/закрытием
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to export page {Page} to image '{Target}'.", CurrentPageIndex, targetPath);
        }
    }
}
