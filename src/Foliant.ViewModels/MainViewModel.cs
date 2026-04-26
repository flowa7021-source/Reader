using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.UseCases;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly OpenDocumentUseCase _openUseCase;
    private readonly Func<IDocument, string, DocumentTabViewModel> _tabFactory;
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

    public MainViewModel(
        OpenDocumentUseCase openUseCase,
        Func<IDocument, string, DocumentTabViewModel> tabFactory,
        ILogger<MainViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(openUseCase);
        ArgumentNullException.ThrowIfNull(tabFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _openUseCase = openUseCase;
        _tabFactory = tabFactory;
        _logger = logger;
    }

    public async Task OpenDocumentFromPathAsync(string path, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(path);

        try
        {
            IDocument document = await _openUseCase.ExecuteAsync(path, ct).ConfigureAwait(false);
            DocumentTabViewModel tab = _tabFactory(document, path);
            Tabs.Add(tab);
            SelectedTab = tab;
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
        }
    }

    [RelayCommand]
    private async Task CloseTabAsync(DocumentTabViewModel? tab)
    {
        if (tab is null)
        {
            return;
        }

        Tabs.Remove(tab);
        await tab.DisposeAsync().ConfigureAwait(false);
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
