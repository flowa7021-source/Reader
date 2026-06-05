using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

/// <summary>
/// «Print» (File → Print, Ctrl+P) — печать текущего документа через системный диалог принтера.
/// VM делегирует всё в <see cref="IPrintService"/> (UI-слой сам показывает PrintDialog, читает
/// выбор пользователя и рендерит страницы) — поэтому здесь нет ни диалогов, ни диапазонов:
/// пользователь выбирает их в системном UI принтера. Команда document-neutral — работает для
/// любого <see cref="Foliant.Domain.IDocument"/>, не только PDF.
/// </summary>
public sealed partial class DocumentTabViewModel
{
    /// <summary>Можно ли распечатать текущий документ: сервис подключён и в документе есть страницы.
    /// Документ-neutral (нет фильтра по расширению), потому что печать строится на
    /// <c>IDocument.RenderPageAsync</c> и работает для PDF/изображений/EPUB/FB2/MOBI/DjVu одинаково.</summary>
    public bool CanPrint => _printService is not null && _document is not null && _document.PageCount > 0;

    /// <summary>Распечатать текущий документ: делегирует в <see cref="IPrintService.PrintAsync"/>,
    /// который сам покажет системный <c>PrintDialog</c> и отправит выбранный диапазон в спулер.
    /// Имя job'а — имя файла без расширения (как видно в очереди печати). Сбой логируется и не
    /// роняет вкладку — как соседние длинные команды (export/PDF-mutate).</summary>
    [RelayCommand(CanExecute = nameof(CanPrint))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Print failure must not crash the tab.")]
    private async Task PrintAsync(CancellationToken ct)
    {
        if (_printService is null || _document is null || !CanPrint)
        {
            return;
        }

        // Имя job'а — имя файла без расширения. GetFileNameWithoutExtension у пути без имени даёт
        // string.Empty (не null), поэтому отдельная проверка не нужна; "Document" — фолбэк на
        // пустую строку, чтобы спулер не показывал безымянный job.
        string jobTitle = Path.GetFileNameWithoutExtension(_filePath);
        if (string.IsNullOrWhiteSpace(jobTitle))
        {
            jobTitle = "Document";
        }

        try
        {
            await _printService.PrintAsync(_document, jobTitle, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // отменено пользователем / закрытием — тихо
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Print failed for '{Path}'.", _filePath);
        }
    }
}
