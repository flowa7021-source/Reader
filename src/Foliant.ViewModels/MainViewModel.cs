using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Foliant.Application.UseCases;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly OpenDocumentUseCase _openUseCase;
    private readonly Func<IDocument, string, DocumentTabViewModel> _tabFactory;
    private readonly IRecentsService _recents;
    private readonly ISettingsService _settings;
    private readonly ILocalizationService _localization;
    private readonly IDocumentIndexer _indexer;
    private readonly ILogger<MainViewModel> _logger;

    [ObservableProperty]
    private string _title = "Foliant";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private DocumentTabViewModel? _selectedTab;

    [ObservableProperty]
    private string _currentTheme = "Light";

    public ObservableCollection<DocumentTabViewModel> Tabs { get; } = [];

    public ObservableCollection<string> RecentFiles { get; } = [];

    /// <summary>Сколько вкладок открыто. Биндится в статус-бар / меню.</summary>
    public int TabsCount => Tabs.Count;

    /// <summary>True если открыта хотя бы одна вкладка. Меню «File → Close All Tabs»
    /// и подобные команды могут отключаться при <c>HasOpenTab == false</c>.</summary>
    public bool HasOpenTab => Tabs.Count > 0;

    public MainViewModel(
        OpenDocumentUseCase openUseCase,
        Func<IDocument, string, DocumentTabViewModel> tabFactory,
        IRecentsService recents,
        ISettingsService settings,
        ILocalizationService localization,
        IDocumentIndexer indexer,
        ILogger<MainViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(openUseCase);
        ArgumentNullException.ThrowIfNull(tabFactory);
        ArgumentNullException.ThrowIfNull(recents);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(indexer);
        ArgumentNullException.ThrowIfNull(logger);

        _openUseCase = openUseCase;
        _tabFactory = tabFactory;
        _recents = recents;
        _settings = settings;
        _localization = localization;
        _indexer = indexer;
        _logger = logger;

        // Tabs.Count → PropertyChanged for TabsCount + HasOpenTab.
        Tabs.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(TabsCount));
            OnPropertyChanged(nameof(HasOpenTab));
        };
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        await _settings.LoadAsync(ct);
        CurrentTheme = _settings.Current.Theme == "Auto" ? "Light" : _settings.Current.Theme;
        _localization.SetCulture(_settings.Current.Language);
        await RefreshRecentsAsync(ct);
    }

    public async Task OpenDocumentFromPathAsync(string path, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(path);

        try
        {
            IDocument document = await _openUseCase.ExecuteAsync(path, ct);
            DocumentTabViewModel tab = _tabFactory(document, path);
            Tabs.Add(tab);
            SelectedTab = tab;

            _indexer.Enqueue(document, path);
            await tab.LoadAnnotationsAsync(ct);
            await tab.LoadBookmarksAsync(ct);
            await _recents.AddAsync(path, ct);
            await RefreshRecentsAsync(ct);
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = ex.Message;
            _logger.LogWarning(ex, "Failed to open document '{Path}'.", path);
        }
        catch (FileNotFoundException ex)
        {
            StatusMessage = ex.Message;
            _logger.LogWarning(ex, "Document not found: '{Path}'.", path);

            await _recents.RemoveAsync(path, ct);
            await RefreshRecentsAsync(ct);
        }
    }

    [RelayCommand]
    private Task OpenRecentAsync(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? Task.CompletedTask
            : OpenDocumentFromPathAsync(path, CancellationToken.None);
    }

    [RelayCommand]
    private async Task ClearRecentsAsync()
    {
        await _recents.ClearAsync(CancellationToken.None);
        await RefreshRecentsAsync(CancellationToken.None);
    }

    private async Task RefreshRecentsAsync(CancellationToken ct)
    {
        IReadOnlyList<string> items = await _recents.GetAsync(ct);
        RecentFiles.Clear();
        foreach (string p in items)
        {
            RecentFiles.Add(p);
        }
    }

    [RelayCommand]
    private async Task CloseTabAsync(DocumentTabViewModel? tab)
    {
        if (tab is null)
        {
            return;
        }

        int removingIndex = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

        // Если закрыли активный таб — переселиться на соседа.
        if (ReferenceEquals(SelectedTab, tab) || SelectedTab is null)
        {
            if (Tabs.Count == 0)
            {
                SelectedTab = null;
            }
            else
            {
                int next = Math.Min(removingIndex, Tabs.Count - 1);
                SelectedTab = Tabs[next];
            }
        }

        await tab.DisposeAsync();
    }

    [RelayCommand]
    private Task CloseCurrentTabAsync() => CloseTabAsync(SelectedTab);

    [RelayCommand]
    private void NextTab()
    {
        if (Tabs.Count <= 1 || SelectedTab is null)
        {
            return;
        }
        int idx = Tabs.IndexOf(SelectedTab);
        SelectedTab = Tabs[(idx + 1) % Tabs.Count];
    }

    [RelayCommand]
    private void PreviousTab()
    {
        if (Tabs.Count <= 1 || SelectedTab is null)
        {
            return;
        }
        int idx = Tabs.IndexOf(SelectedTab);
        SelectedTab = Tabs[(idx - 1 + Tabs.Count) % Tabs.Count];
    }

    /// <summary>
    /// Принимает 1-based номер вкладки (как пользователь видит на клавиатуре —
    /// Ctrl+1..Ctrl+9). Если такого индекса нет — no-op.
    /// </summary>
    [RelayCommand]
    private void SelectTabByNumber(int oneBasedIndex)
    {
        int zeroBased = oneBasedIndex - 1;
        if (zeroBased < 0 || zeroBased >= Tabs.Count)
        {
            return;
        }
        SelectedTab = Tabs[zeroBased];
    }

    [RelayCommand]
    private void ChangeTheme(string? themeName)
    {
        if (themeName is null)
        {
            return;
        }

        CurrentTheme = themeName;
    }

    partial void OnSelectedTabChanged(DocumentTabViewModel? oldValue, DocumentTabViewModel? newValue)
    {
        Title = newValue is null ? "Foliant" : $"{newValue.Title} — Foliant";

        if (newValue is not null)
        {
            _ = newValue.RenderCurrentPageAsync(CancellationToken.None);
        }
    }

    partial void OnCurrentThemeChanged(string value)
    {
        RenderTheme renderTheme = value switch
        {
            "Dark" => RenderTheme.Dark,
            "HighContrast" => RenderTheme.HighContrast,
            _ => RenderTheme.Original,
        };

        foreach (DocumentTabViewModel tab in Tabs)
        {
            tab.Theme = renderTheme;
            _ = tab.RenderCurrentPageAsync(CancellationToken.None);
        }
    }
}
