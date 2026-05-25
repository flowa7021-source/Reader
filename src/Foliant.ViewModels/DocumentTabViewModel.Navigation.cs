using CommunityToolkit.Mvvm.Input;

namespace Foliant.ViewModels;

public sealed partial class DocumentTabViewModel
{
    /// <summary>Stack страниц, на которых пользователь был раньше (top — самая свежая).</summary>
    private readonly Stack<int> _navBack = new();
    /// <summary>Stack страниц, отменённых через Back (готовы к Forward).</summary>
    private readonly Stack<int> _navForward = new();
    /// <summary>True пока активна команда Go-Back / Go-Forward — чтобы свой собственный пуш в стек
    /// не делать в OnCurrentPageIndexChanged.</summary>
    private bool _navigatingHistory;

    public bool CanGoBack => _navBack.Count > 0;

    public bool CanGoForward => _navForward.Count > 0;

    partial void OnCurrentPageIndexChanged(int oldValue, int newValue)
    {
        int clamped = Math.Clamp(newValue, 0, Math.Max(0, PageCount - 1));
        if (clamped != newValue)
        {
            CurrentPageIndex = clamped;
            return;
        }

        // Если переход не из Back/Forward команды — это «новая» навигация:
        // запоминаем откуда ушли, ресет forward-стека (как в браузере).
        if (!_navigatingHistory && oldValue != newValue)
        {
            _navBack.Push(oldValue);
            _navForward.Clear();
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
        }

        RefreshCurrentPageAnnotations();
        OnPropertyChanged(nameof(VisiblePageIndices));
        ApplyFitIfNeeded();
        _ = RenderCurrentPageAsync(CancellationToken.None);
    }

    [RelayCommand]
    private void GoBack()
    {
        if (_navBack.Count == 0)
        {
            return;
        }

        int previous = _navBack.Pop();
        _navForward.Push(CurrentPageIndex);
        _navigatingHistory = true;
        try
        {
            CurrentPageIndex = previous;
        }
        finally
        {
            _navigatingHistory = false;
        }
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
    }

    [RelayCommand]
    private void GoForward()
    {
        if (_navForward.Count == 0)
        {
            return;
        }

        int next = _navForward.Pop();
        _navBack.Push(CurrentPageIndex);
        _navigatingHistory = true;
        try
        {
            CurrentPageIndex = next;
        }
        finally
        {
            _navigatingHistory = false;
        }
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
    }

    /// <summary>Очистить back/forward стеки. UI: «Reset navigation history».</summary>
    [RelayCommand]
    private void ClearNavigationHistory()
    {
        if (_navBack.Count == 0 && _navForward.Count == 0)
        {
            return;
        }
        _navBack.Clear();
        _navForward.Clear();
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
    }

    [RelayCommand]
    private void NextPage()
    {
        if (CurrentPageIndex < PageCount - 1)
        {
            CurrentPageIndex++;
        }
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (CurrentPageIndex > 0)
        {
            CurrentPageIndex--;
        }
    }

    [RelayCommand]
    private void FirstPage() => CurrentPageIndex = 0;

    [RelayCommand]
    private void LastPage() => CurrentPageIndex = Math.Max(0, PageCount - 1);

    /// <summary>Принимает 1-based номер страницы (как у пользователя в UI), переводит в 0-based индекс.</summary>
    [RelayCommand]
    private void GoToPage(int pageNumber)
    {
        CurrentPageIndex = Math.Clamp(pageNumber - 1, 0, Math.Max(0, PageCount - 1));
    }
}
