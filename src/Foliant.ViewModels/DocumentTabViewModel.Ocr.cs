using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

public sealed partial class DocumentTabViewModel
{
    private CancellationTokenSource? _ocrCts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunOcrCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelOcrCommand))]
    private bool _isOcrRunning;

    /// <summary>Короткий статус OCR для статус-бара: «3/10», «OCR done», «cancelled», «failed».</summary>
    [ObservableProperty]
    private string? _ocrStatus;

    /// <summary>Распознанные текстовые слои (по странице). null до первого запуска OCR.</summary>
    public IReadOnlyList<TextLayer>? OcrLayers { get; private set; }

    /// <summary>OCR доступен только если движок и fingerprint предоставлены (DI),
    /// документ непустой и распознавание сейчас не идёт.</summary>
    public bool CanRunOcr => _ocr is not null && _fingerprint is not null && !IsOcrRunning && PageCount > 0;

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "User-triggered OCR must surface as a status message, not crash the app.")]
    [RelayCommand(CanExecute = nameof(CanRunOcr))]
    private async Task RunOcrAsync()
    {
        if (_ocr is null || _fingerprint is null)
        {
            return;
        }

        _ocrCts = new CancellationTokenSource();
        IsOcrRunning = true;
        OcrStatus = $"0/{PageCount}";
        try
        {
            string fp = await _fingerprint.ComputeAsync(_filePath, _ocrCts.Token);
            var progress = new Progress<OcrProgress>(p => OcrStatus = $"{p.CompletedPages}/{p.TotalPages}");
            OcrLayers = await _ocr.RecognizeDocumentAsync(_document, fp, new OcrOptions(), progress, _ocrCts.Token);
            OcrStatus = $"OCR: {OcrLayers.Count} pages";
        }
        catch (OperationCanceledException)
        {
            OcrStatus = "OCR cancelled";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR failed for {Path}", _filePath);
            OcrStatus = "OCR failed";
        }
        finally
        {
            IsOcrRunning = false;
            _ocrCts?.Dispose();
            _ocrCts = null;
        }
    }

    private bool CanCancelOcr => IsOcrRunning;

    [RelayCommand(CanExecute = nameof(CanCancelOcr))]
    private void CancelOcr() => _ocrCts?.Cancel();
}
