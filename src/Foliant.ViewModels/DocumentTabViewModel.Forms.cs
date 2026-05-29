using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

/// <summary>
/// Form-data commands (Q-F24 write-back). Read path использует уже-готовый
/// <see cref="IPdfFormReader"/>, write path — новый <see cref="IPdfFormFillService"/>.
/// Сериализация в JSON / FDF / XFDF — через зарегистрированные <see cref="IFormDataExporter"/>/
/// <see cref="IFormDataImporter"/> в <see cref="IFormDataFormatCatalog"/>.
/// View собирает оба пути через единую schema: ExportFormDataRequest (исходный PDF →
/// файл данных) / ImportFormDataRequest (файл данных + исходный PDF → новый PDF).
/// </summary>
public sealed partial class DocumentTabViewModel
{
    /// <summary>Можно ли экспортировать form-данные: документ — PDF, формат-catalog
    /// зарегистрирован, есть read-сервис. Менее строго чем write: read fail-soft.</summary>
    public bool CanExportFormData =>
        _pdfFormReader is not null
        && _formDataFormatCatalog is not null
        && Path.GetExtension(_filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Можно ли импортировать form-данные: документ — PDF, есть write-сервис
    /// и формат-catalog.</summary>
    public bool CanImportFormData =>
        _pdfFormFillService is not null
        && _formDataFormatCatalog is not null
        && Path.GetExtension(_filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Прочитать текущие form-значения из исходного PDF, сериализовать через
    /// exporter, выбранный по расширению <paramref name="targetPath"/>, и записать в файл.
    /// Битый/без-формы PDF → пустой словарь, пишем пустой sentinel.</summary>
    [RelayCommand(CanExecute = nameof(CanExportFormData))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Form-data export failure must not crash the tab.")]
    private async Task ExportFormDataAsync(string? targetPath, CancellationToken ct)
    {
        if (_pdfFormReader is null
            || _formDataFormatCatalog is null
            || string.IsNullOrWhiteSpace(targetPath)
            || !CanExportFormData)
        {
            return;
        }

        var exporter = _formDataFormatCatalog.ResolveExporter(targetPath);
        if (exporter is null)
        {
            _logger.LogWarning("No form-data exporter registered for '{Path}'.", targetPath);
            return;
        }

        try
        {
            var values = await _pdfFormReader.ReadAsync(_filePath, ct).ConfigureAwait(false);
            string content = exporter.Export(values);
            await File.WriteAllTextAsync(targetPath, content, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to export form data from '{Path}' to '{Target}'.", _filePath, targetPath);
        }
    }

    /// <summary>Прочитать form-значения из файла данных (<see cref="ImportFormDataRequest.SourceDataPath"/>),
    /// применить их к исходному PDF и сохранить в новый PDF
    /// (<see cref="ImportFormDataRequest.TargetPdfPath"/>). Имена полей, отсутствующие в PDF,
    /// игнорируются — это нормально для пачек.</summary>
    [RelayCommand(CanExecute = nameof(CanImportFormData))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Form-data import failure must not crash the tab.")]
    private async Task ImportFormDataAsync(ImportFormDataRequest? request, CancellationToken ct)
    {
        if (_pdfFormFillService is null
            || _formDataFormatCatalog is null
            || request is null
            || string.IsNullOrWhiteSpace(request.SourceDataPath)
            || string.IsNullOrWhiteSpace(request.TargetPdfPath)
            || !CanImportFormData)
        {
            return;
        }

        var importer = _formDataFormatCatalog.ResolveImporter(request.SourceDataPath);
        if (importer is null)
        {
            _logger.LogWarning("No form-data importer registered for '{Path}'.", request.SourceDataPath);
            return;
        }

        try
        {
            string content = await File.ReadAllTextAsync(request.SourceDataPath, ct).ConfigureAwait(false);
            var values = importer.Import(content);
            await _pdfFormFillService.ApplyAsync(_filePath, values, request.TargetPdfPath, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to import form data from '{Source}' into '{Target}'.",
                request.SourceDataPath, request.TargetPdfPath);
        }
    }
}

/// <summary>View-supplied envelope для <c>ImportFormDataCommand</c>: путь к файлу с данными +
/// путь к выходному PDF. Source PDF — это <c>FilePath</c> текущей вкладки.</summary>
public sealed record ImportFormDataRequest(string SourceDataPath, string TargetPdfPath);
