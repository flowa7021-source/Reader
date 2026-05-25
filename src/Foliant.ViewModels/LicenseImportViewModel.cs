using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Foliant.Domain;

namespace Foliant.ViewModels;

/// <summary>
/// Модальный диалог импорта лицензии. Пользователь вставляет JSON-блок и
/// base64-подпись (две textarea); кнопка <c>Import</c> вызывает
/// <see cref="ILicenseManager.ImportAsync"/>; результат показывается в
/// <see cref="LastResult"/>. Ошибки I/O не пробрасываются — сообщение в
/// <see cref="ErrorMessage"/> для UI-баннера.
/// </summary>
public sealed partial class LicenseImportViewModel : ObservableObject
{
    private readonly ILicenseManager _manager;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    private string _licenseJson = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    private string _signatureBase64 = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WasImportedSuccessfully))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    private LicenseValidationResult? _lastResult;

    [ObservableProperty]
    private bool _isImporting;

    /// <summary>Сообщение об ошибке для отображения в UI; <c>null</c> если ошибки нет.
    /// Сбрасывается на каждом <see cref="ImportAsync"/> и <see cref="Clear"/>.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    private string? _errorMessage;

    public bool WasImportedSuccessfully => LastResult?.Status == LicenseStatus.Valid;

    public LicenseImportViewModel(ILicenseManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        _manager = manager;
    }

    private bool CanImport() =>
        !string.IsNullOrWhiteSpace(LicenseJson) && !string.IsNullOrWhiteSpace(SignatureBase64) && !IsImporting;

    [RelayCommand(CanExecute = nameof(CanImport))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Import failure must surface as ErrorMessage, not crash the dialog.")]
    private async Task ImportAsync()
    {
        IsImporting = true;
        ErrorMessage = null;
        try
        {
            LastResult = await _manager.ImportAsync(LicenseJson.Trim(), SignatureBase64.Trim(), CancellationToken.None);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            LastResult = LicenseValidationResult.Invalid(ex.Message);
        }
        finally
        {
            IsImporting = false;
        }
    }

    private bool CanClear() =>
        !IsImporting &&
        (!string.IsNullOrEmpty(LicenseJson)
         || !string.IsNullOrEmpty(SignatureBase64)
         || LastResult is not null
         || ErrorMessage is not null);

    /// <summary>Очистить оба поля ввода и сбросить <see cref="LastResult"/> /
    /// <see cref="ErrorMessage"/>. Не активна во время импорта.</summary>
    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        LicenseJson = string.Empty;
        SignatureBase64 = string.Empty;
        LastResult = null;
        ErrorMessage = null;
    }
}
