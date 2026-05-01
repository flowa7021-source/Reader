using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Foliant.Domain;

namespace Foliant.ViewModels;

/// <summary>
/// Модальный диалог импорта лицензии. Пользователь вставляет JSON-блок и
/// base64-подпись (две textarea); кнопка <c>Import</c> вызывает
/// <see cref="ILicenseManager.ImportAsync"/>; результат показывается в
/// <see cref="LastResult"/>.
/// </summary>
public sealed partial class LicenseImportViewModel : ObservableObject
{
    private readonly ILicenseManager _manager;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private string _licenseJson = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private string _signatureBase64 = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WasImportedSuccessfully))]
    private LicenseValidationResult? _lastResult;

    [ObservableProperty]
    private bool _isImporting;

    public bool WasImportedSuccessfully => LastResult?.Status == LicenseStatus.Valid;

    public LicenseImportViewModel(ILicenseManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        _manager = manager;
    }

    private bool CanImport() =>
        !string.IsNullOrWhiteSpace(LicenseJson) && !string.IsNullOrWhiteSpace(SignatureBase64) && !IsImporting;

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        IsImporting = true;
        try
        {
            LastResult = await _manager.ImportAsync(LicenseJson.Trim(), SignatureBase64.Trim(), CancellationToken.None);
        }
        finally
        {
            IsImporting = false;
        }
    }
}
