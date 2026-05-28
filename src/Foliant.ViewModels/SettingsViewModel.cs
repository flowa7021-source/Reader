using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Foliant.Application.Settings;

namespace Foliant.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly ILocalizationService _localization;

    [ObservableProperty]
    private string _selectedTheme = "Light";

    [ObservableProperty]
    private string _selectedLanguage = "ru";

    [ObservableProperty]
    private double _diskCacheLimitGb = 5.0;

    [ObservableProperty]
    private bool _clearCacheOnExit;

    [ObservableProperty]
    private string _ocrLanguage = "rus+eng";

    [ObservableProperty]
    private int _maxParallelOcrPages = 4;

    [ObservableProperty]
    private bool _autoOcrOpenedScans;

    [ObservableProperty]
    private OcrModelTier _ocrModelTier = OcrModelTier.Basic;

    [ObservableProperty]
    private bool _checkForUpdates = true;

    [ObservableProperty]
    private bool _crashReportingEnabled;

    [ObservableProperty]
    private string _defaultAnnotationAuthor = string.Empty;

    [ObservableProperty]
    private bool _isSaved;

    public IReadOnlyList<string> AvailableThemes { get; } = ["Auto", "Light", "Dark", "HighContrast"];

    public IReadOnlyList<string> AvailableLanguages { get; } = ["ru", "en"];

    public IReadOnlyList<OcrModelTier> AvailableOcrTiers { get; } =
        [OcrModelTier.Basic, OcrModelTier.Standard, OcrModelTier.Full];

    public SettingsViewModel(ISettingsService settingsService, ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(localization);
        _settingsService = settingsService;
        _localization = localization;
        LoadFromCurrent();
    }

    public void LoadFromCurrent()
    {
        AppSettings s = _settingsService.Current;
        SelectedTheme = s.Theme;
        SelectedLanguage = s.Language;
        DiskCacheLimitGb = s.Cache.DiskLimitBytes / (1024.0 * 1024 * 1024);
        ClearCacheOnExit = s.Cache.ClearOnExit;
        OcrLanguage = s.Ocr.DefaultLanguage;
        MaxParallelOcrPages = s.Ocr.MaxParallelPages;
        AutoOcrOpenedScans = s.Ocr.AutoOcrOpenedScans;
        OcrModelTier = s.Ocr.ModelTier;
        CheckForUpdates = s.CheckForUpdates;
        CrashReportingEnabled = s.CrashReportingEnabled;
        DefaultAnnotationAuthor = s.DefaultAnnotationAuthor ?? string.Empty;
        IsSaved = false;
    }

    partial void OnSelectedThemeChanged(string value) => IsSaved = false;

    partial void OnSelectedLanguageChanged(string value) => IsSaved = false;

    partial void OnDiskCacheLimitGbChanged(double value) => IsSaved = false;

    partial void OnClearCacheOnExitChanged(bool value) => IsSaved = false;

    partial void OnOcrLanguageChanged(string value) => IsSaved = false;

    partial void OnMaxParallelOcrPagesChanged(int value) => IsSaved = false;

    partial void OnAutoOcrOpenedScansChanged(bool value) => IsSaved = false;

    partial void OnOcrModelTierChanged(OcrModelTier value) => IsSaved = false;

    partial void OnCheckForUpdatesChanged(bool value) => IsSaved = false;

    partial void OnCrashReportingEnabledChanged(bool value) => IsSaved = false;

    partial void OnDefaultAnnotationAuthorChanged(string value) => IsSaved = false;

    [RelayCommand]
    private async Task SaveAsync()
    {
        // UpdateAsync (а не SaveAsync с заранее собранным снимком): мутация идёт под одной
        // блокировкой поверх актуального Current, поэтому RecentFiles, добавленные параллельно,
        // не затираются (lost-update).
        await _settingsService.UpdateAsync(current => current with
        {
            Theme = SelectedTheme,
            Language = SelectedLanguage,
            Cache = current.Cache with
            {
                DiskLimitBytes = (long)(DiskCacheLimitGb * 1024 * 1024 * 1024),
                ClearOnExit = ClearCacheOnExit,
            },
            Ocr = current.Ocr with
            {
                DefaultLanguage = OcrLanguage,
                MaxParallelPages = MaxParallelOcrPages,
                AutoOcrOpenedScans = AutoOcrOpenedScans,
                ModelTier = OcrModelTier,
            },
            CheckForUpdates = CheckForUpdates,
            CrashReportingEnabled = CrashReportingEnabled,
            DefaultAnnotationAuthor = string.IsNullOrWhiteSpace(DefaultAnnotationAuthor) ? null : DefaultAnnotationAuthor.Trim(),
        }, CancellationToken.None);

        // Hot-switch культуры — все XAML-биндинги {Path=[Key]} обновятся через "Item[]" PropertyChanged.
        if (!string.Equals(_localization.CurrentCulture, SelectedLanguage, StringComparison.OrdinalIgnoreCase))
        {
            _localization.SetCulture(SelectedLanguage);
        }

        IsSaved = true;
    }

    /// <summary>Сбросить все поля к заводским настройкам (<see cref="AppSettings.Default"/>).
    /// Не сохраняет немедленно — пользователь должен нажать Save, чтобы изменения
    /// попали на диск. <see cref="IsSaved"/> сбрасывается автоматически через хуки.</summary>
    [RelayCommand]
    private void ResetToDefaults()
    {
        var defaults = AppSettings.Default;
        SelectedTheme = defaults.Theme;
        SelectedLanguage = defaults.Language;
        DiskCacheLimitGb = defaults.Cache.DiskLimitBytes / (1024.0 * 1024 * 1024);
        ClearCacheOnExit = defaults.Cache.ClearOnExit;
        OcrLanguage = defaults.Ocr.DefaultLanguage;
        MaxParallelOcrPages = defaults.Ocr.MaxParallelPages;
        AutoOcrOpenedScans = defaults.Ocr.AutoOcrOpenedScans;
        OcrModelTier = defaults.Ocr.ModelTier;
        CheckForUpdates = defaults.CheckForUpdates;
        CrashReportingEnabled = defaults.CrashReportingEnabled;
        DefaultAnnotationAuthor = defaults.DefaultAnnotationAuthor ?? string.Empty;
    }
}
