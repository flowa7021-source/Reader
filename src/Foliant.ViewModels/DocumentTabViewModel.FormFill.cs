using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

/// <summary>
/// Интерактивное заполнение AcroForm-полей (Q-F24 UI). В отличие от Export/Import-команд в
/// <see cref="DocumentTabViewModel"/> (которые ходят через файлы данных FDF/XFDF/JSON),
/// этот путь читает поля прямо из PDF (<see cref="IPdfFormReader"/>), отдаёт их View для
/// inline-редактирования и пишет изменённые значения в новый PDF (<see cref="IPdfFormFillService"/>).
/// </summary>
public sealed partial class DocumentTabViewModel
{
    /// <summary>Можно ли интерактивно заполнять форму: документ — PDF, есть и read-, и
    /// write-сервис. Catalog не нужен (значения берутся прямо из PDF, не из файла данных).</summary>
    public bool CanFillForm =>
        _pdfFormReader is not null
        && _pdfFormFillService is not null
        && Path.GetExtension(_filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Прочитать текущие form-поля из исходного PDF для inline-редактирования.
    /// Пустой/без-формы PDF → пустой список. Имена сортируются для стабильного UI-порядка
    /// (reader порядок не гарантирует).</summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Form-field read failure must surface as empty list, not crash the dialog.")]
    public async Task<IReadOnlyList<FormFieldEntry>> ReadFormFieldsAsync(CancellationToken ct)
    {
        if (_pdfFormReader is null || !CanFillForm)
        {
            return [];
        }

        try
        {
            var values = await _pdfFormReader.ReadAsync(_filePath, ct).ConfigureAwait(false);
            return [.. values
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => new FormFieldEntry(kv.Key, kv.Value))];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read form fields from '{Path}'.", _filePath);
            return [];
        }
    }

    /// <summary>Применить отредактированные значения к исходному PDF и сохранить в новый файл.
    /// No-op при отсутствии write-сервиса или пустом target. Имена полей, которых нет в PDF,
    /// игнорируются сервисом (best-effort).</summary>
    [RelayCommand(CanExecute = nameof(CanFillForm))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Form-fill failure must not crash the tab.")]
    private async Task FillFormAsync(FillFormRequest? request, CancellationToken ct)
    {
        if (_pdfFormFillService is null
            || request is null
            || request.Fields is null
            || string.IsNullOrWhiteSpace(request.TargetPdfPath)
            || !CanFillForm)
        {
            return;
        }

        var values = request.Fields.ToDictionary(f => f.Name, f => f.Value, StringComparer.Ordinal);

        try
        {
            await _pdfFormFillService.ApplyAsync(_filePath, values, request.TargetPdfPath, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // user-cancelled — ignore
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fill form from '{Source}' into '{Target}'.", _filePath, request.TargetPdfPath);
        }
    }
}

/// <summary>Одно form-поле для inline-редактирования: имя (read-only) + текущее значение
/// (редактируемое во View). Mutable <see cref="Value"/> — View пишет туда правки пользователя.</summary>
public sealed class FormFieldEntry(string name, string value)
{
    public string Name { get; } = name;
    public string Value { get; set; } = value;
}

/// <summary>View-supplied envelope для <c>FillFormCommand</c>: отредактированные поля +
/// путь к выходному PDF. Source PDF — это <c>FilePath</c> текущей вкладки.</summary>
public sealed record FillFormRequest(IReadOnlyList<FormFieldEntry> Fields, string TargetPdfPath);
