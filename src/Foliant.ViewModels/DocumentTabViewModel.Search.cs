using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.ViewModels;

public sealed partial class DocumentTabViewModel
{
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Case-sensitive поиск; default false. Биндится к чекбоксу в search-sidebar.</summary>
    [ObservableProperty]
    private bool _searchMatchCase;

    /// <summary>Whole-word поиск; default false. Биндится к чекбоксу в search-sidebar.</summary>
    [ObservableProperty]
    private bool _searchMatchWholeWord;

    [ObservableProperty]
    private bool _isSearchVisible;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedSearchHitOneBasedIndex))]
    [NotifyPropertyChangedFor(nameof(SearchHitInfo))]
    private SearchHit? _selectedSearchHit;

    public ObservableCollection<SearchHit> SearchResults { get; } = [];

    /// <summary>Session-history search-запросов (most-recent first), capped at <see cref="MaxRecentSearches"/>. Не персистится.</summary>
    public ObservableCollection<string> RecentSearches { get; } = [];

    /// <summary>Сколько последних поисков держим в памяти на вкладку.</summary>
    public const int MaxRecentSearches = 10;

    /// <summary>Сколько хитов в текущем поисковом результате. Биндится в статус-бар
    /// (рядом с PageInfo / ZoomPercent) и индикатор search-sidebar.</summary>
    public int SearchHitCount => SearchResults.Count;

    /// <summary>1-based индекс выбранного хита в <see cref="SearchResults"/>; 0 если ничего не выбрано
    /// или коллекция пуста. Удобен для XAML-биндинга в стиле «3 / 12».</summary>
    public int SelectedSearchHitOneBasedIndex
    {
        get
        {
            if (SelectedSearchHit is null || SearchResults.Count == 0)
            {
                return 0;
            }
            int idx = SearchResults.IndexOf(SelectedSearchHit);
            return idx < 0 ? 0 : idx + 1;
        }
    }

    /// <summary>Строка вида «3/12» (или пустая, если поиска не было). Локаль-агностичная.</summary>
    public string SearchHitInfo
    {
        get
        {
            if (SearchResults.Count == 0)
            {
                return string.Empty;
            }
            return $"{SelectedSearchHitOneBasedIndex}/{SearchResults.Count}";
        }
    }

    /// <summary>
    /// Pull-to-front + cap. Регистро-нечувствительный дедуп: ввод "Cat" и "cat"
    /// считаются одной записью; новый занимает место старого. Если зарегистрирован
    /// <see cref="ISearchHistoryService"/>, вызов также обновляет shared-историю —
    /// другие вкладки увидят новый запрос при следующем чтении.
    /// </summary>
    private void PromoteRecentSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        for (int i = RecentSearches.Count - 1; i >= 0; i--)
        {
            if (string.Equals(RecentSearches[i], query, StringComparison.OrdinalIgnoreCase))
            {
                RecentSearches.RemoveAt(i);
            }
        }
        RecentSearches.Insert(0, query);
        while (RecentSearches.Count > MaxRecentSearches)
        {
            RecentSearches.RemoveAt(RecentSearches.Count - 1);
        }

        _searchHistory?.Add(query);
    }

    /// <summary>Команда заполняет <see cref="SearchText"/> из истории и сразу запускает поиск.</summary>
    [RelayCommand]
    private async Task SelectRecentSearchAsync(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }
        SearchText = query;
        await RunSearchAsync();
    }

    /// <summary>Перейти к следующему хиту в <see cref="SearchResults"/>; wrap при достижении конца.</summary>
    [RelayCommand]
    private void NextSearchHit()
    {
        if (SearchResults.Count == 0)
        {
            return;
        }
        int idx = SelectedSearchHit is null ? -1 : SearchResults.IndexOf(SelectedSearchHit);
        SelectedSearchHit = SearchResults[(idx + 1) % SearchResults.Count];
    }

    /// <summary>Перейти к предыдущему хиту; wrap в начале.</summary>
    [RelayCommand]
    private void PreviousSearchHit()
    {
        if (SearchResults.Count == 0)
        {
            return;
        }
        int idx = SelectedSearchHit is null ? 0 : SearchResults.IndexOf(SelectedSearchHit);
        SelectedSearchHit = SearchResults[(idx - 1 + SearchResults.Count) % SearchResults.Count];
    }

    partial void OnSelectedSearchHitChanged(SearchHit? value)
    {
        if (value is not null && value.PageIndex != CurrentPageIndex)
        {
            // OnCurrentPageIndexChanged подхватит и refilter аннотаций, и render.
            CurrentPageIndex = value.PageIndex;
        }
    }

    [RelayCommand]
    private void ToggleSearch()
    {
        IsSearchVisible = !IsSearchVisible;
    }

    [RelayCommand]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Search error must not crash the tab.")]
    private async Task RunSearchAsync()
    {
        SearchResults.Clear();
        SelectedSearchHit = null;

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            ClearSearchHighlights();
            return;
        }

        IsSearching = true;
        string trimmed = SearchText.Trim();
        try
        {
            var query = new SearchQuery(
                trimmed,
                MatchCase: SearchMatchCase,
                MatchWholeWord: SearchMatchWholeWord);
            IReadOnlyList<SearchHit> hits = await _searchService
                .SearchInDocumentAsync(_document, _filePath, query, CancellationToken.None);

            foreach (SearchHit hit in hits)
            {
                SearchResults.Add(hit);
            }
            PromoteRecentSearch(trimmed);

            _activeHighlightQuery = trimmed;
            _activeHighlightMatchCase = SearchMatchCase;
            _activeHighlightWholeWord = SearchMatchWholeWord;
            await RefreshSearchHighlightsAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not an error.
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _logger.LogWarning(ex, "Search failed for '{Query}' in '{Title}'.", SearchText, Title);
        }
        finally
        {
            IsSearching = false;
        }
    }
}
