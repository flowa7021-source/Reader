using System.Diagnostics.CodeAnalysis;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

public sealed partial class DocumentTabViewModel
{
    /// <summary>Прогресс batch-экспорта (0..N страниц); 0 в покое. Биндится status bar'ом / UI'ем,
    /// чтобы user видел продвижение длинного экспорта.</summary>
    [ObservableProperty]
    private int _pageImageExportProgress;

    /// <summary>Всего страниц в текущем batch'е (0 в покое). Используется UI в паре с
    /// <see cref="PageImageExportProgress"/> для отрисовки "{n}/{total}".</summary>
    [ObservableProperty]
    private int _pageImageExportTotal;

    /// <summary>true пока идёт batch-экспорт. Меню/кнопка batch должны быть disabled,
    /// чтобы исключить запуск второго экспорта поверх первого.</summary>
    public bool IsPageImageExportRunning => PageImageExportTotal > 0;

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

    /// <summary>true когда batch-экспорт всех страниц применим: экспортер есть, документ не пуст,
    /// и сейчас не идёт другой batch (защита от двойного запуска).</summary>
    public bool CanExportAllPagesAsImages =>
        _pageImageExporter is not null && PageCount > 0 && !IsPageImageExportRunning;

    /// <summary>Batch-экспорт всех страниц документа в указанную папку. Формат — PNG (хардкод;
    /// JPEG-batch — отдельный пункт меню, когда добавим). Имена файлов: <c>{docStem}-pNNN.png</c>
    /// с zero-padded шириной по числу страниц. Прогресс отражается в <see cref="PageImageExportProgress"/>
    /// для UI; cancellation между страницами; сбой одной страницы прерывает batch и логируется.</summary>
    [RelayCommand(CanExecute = nameof(CanExportAllPagesAsImages))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Batch-export failure must not crash the tab.")]
    private async Task ExportAllPagesAsImagesAsync(string? targetDirectory, CancellationToken ct)
    {
        if (_pageImageExporter is null || string.IsNullOrWhiteSpace(targetDirectory) || PageCount <= 0)
        {
            return;
        }

        string stem = Path.GetFileNameWithoutExtension(_filePath);
        PageImageExportTotal = PageCount;
        PageImageExportProgress = 0;
        OnPropertyChanged(nameof(IsPageImageExportRunning));
        OnPropertyChanged(nameof(CanExportAllPagesAsImages));
        ExportAllPagesAsImagesCommand.NotifyCanExecuteChanged();

        var progress = new Progress<int>(done => PageImageExportProgress = done);

        try
        {
            int written = await _pageImageExporter.ExportRangeAsync(
                _document,
                firstPageIndex: 0,
                lastPageIndexInclusive: PageCount - 1,
                zoom: Zoom,
                targetDirectory: targetDirectory,
                format: "png",
                namePrefix: string.IsNullOrWhiteSpace(stem) ? "page" : stem,
                progress: progress,
                ct: ct).ConfigureAwait(false);

            _logger.LogInformation("Exported {Count} pages of '{Source}' as images into '{Target}'.",
                written, _filePath, targetDirectory);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Batch image export cancelled at page {Page}.", PageImageExportProgress);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Batch image export failed after page {Page}.", PageImageExportProgress);
        }
        finally
        {
            PageImageExportTotal = 0;
            PageImageExportProgress = 0;
            OnPropertyChanged(nameof(IsPageImageExportRunning));
            OnPropertyChanged(nameof(CanExportAllPagesAsImages));
            ExportAllPagesAsImagesCommand.NotifyCanExecuteChanged();
        }
    }
}
