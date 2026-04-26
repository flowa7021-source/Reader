using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Foliant.Application.Settings;

namespace Foliant.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;

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

    public SettingsViewModel(ISettingsService settingsService)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        _settingsService = settingsService;
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

        await _settingsService.SaveAsync(updated, CancellationToken.None).ConfigureAwait(false);
        IsSaved = true;
    }
}
