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
    private bool _isSaved;

    public IReadOnlyList<string> AvailableThemes { get; } = ["Auto", "Light", "Dark", "HighContrast"];

    public IReadOnlyList<string> AvailableLanguages { get; } = ["ru", "en"];

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
        IsSaved = false;
    }

    partial void OnSelectedThemeChanged(string value) => IsSaved = false;

    partial void OnSelectedLanguageChanged(string value) => IsSaved = false;

    partial void OnDiskCacheLimitGbChanged(double value) => IsSaved = false;

    partial void OnClearCacheOnExitChanged(bool value) => IsSaved = false;

    [RelayCommand]
    private async Task SaveAsync()
    {
        AppSettings updated = _settingsService.Current with
        {
            Theme = SelectedTheme,
            Language = SelectedLanguage,
            Cache = _settingsService.Current.Cache with
            {
                DiskLimitBytes = (long)(DiskCacheLimitGb * 1024 * 1024 * 1024),
                ClearOnExit = ClearCacheOnExit,
            },
        };

        await _settingsService.SaveAsync(updated, CancellationToken.None);

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
    }
}
