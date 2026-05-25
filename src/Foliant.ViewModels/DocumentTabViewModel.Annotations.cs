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
    private readonly List<Annotation> _allAnnotations = [];

    [ObservableProperty]
    private AnnotationFilterMode _annotationFilter = AnnotationFilterMode.All;

    public ObservableCollection<Annotation> CurrentPageAnnotations { get; } = [];

    /// <summary>Общее число аннотаций по всему документу. Обновляется при load/add/remove.</summary>
    public int TotalAnnotationsCount => _allAnnotations.Count;

    /// <summary>Сколько highlight-аннотаций по всему документу.</summary>
    public int HighlightCount => CountByKind(AnnotationKind.Highlight);

    /// <summary>Сколько sticky-note аннотаций.</summary>
    public int NoteCount => CountByKind(AnnotationKind.StickyNote);

    /// <summary>Сколько freehand-аннотаций.</summary>
    public int FreehandCount => CountByKind(AnnotationKind.Freehand);

    private int CountByKind(AnnotationKind kind)
    {
        int count = 0;
        foreach (var a in _allAnnotations)
        {
            if (a.Kind == kind)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>Хелпер: вызывается после любой мутации <c>_allAnnotations</c>; рейзит
    /// <see cref="PropertyChanged"/> для всех зависимых count-property.</summary>
    private void NotifyAnnotationCountsChanged()
    {
        OnPropertyChanged(nameof(TotalAnnotationsCount));
        OnPropertyChanged(nameof(HighlightCount));
        OnPropertyChanged(nameof(NoteCount));
        OnPropertyChanged(nameof(FreehandCount));
    }

    /// <summary>Число аннотаций именно на текущей странице. Совпадает с <c>CurrentPageAnnotations.Count</c>,
    /// но отдельным property удобнее биндить — counter в sidebar/status-bar не должен подписываться
    /// на <c>CollectionChanged</c>.</summary>
    public int CurrentPageAnnotationsCount => CurrentPageAnnotations.Count;

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Annotation load failure must not crash the tab.")]
    public async Task LoadAnnotationsAsync(CancellationToken ct)
    {
        try
        {
            var loaded = await _annotationService.ListAsync(_filePath, ct);
            _allAnnotations.Clear();
            _allAnnotations.AddRange(loaded);
            NotifyAnnotationCountsChanged();
            RefreshCurrentPageAnnotations();
        }
        catch (OperationCanceledException)
        {
            // shutdown — игнорируем
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load annotations for '{Path}'.", _filePath);
        }
    }

    public async Task AddHighlightAsync(int pageIndex, AnnotationRect bounds, string colorHex, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(colorHex);

        var hl = Annotation.Highlight(pageIndex, bounds, colorHex, DateTimeOffset.UtcNow);
        await _annotationService.AddAsync(_filePath, hl, ct);
        _allAnnotations.Add(hl);
        NotifyAnnotationCountsChanged();
        if (pageIndex == CurrentPageIndex && MatchesFilter(hl))
        {
            CurrentPageAnnotations.Add(hl);
        }
    }

    public async Task AddNoteAsync(int pageIndex, AnnotationRect bounds, string text, string colorHex, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(colorHex);

        var note = Annotation.StickyNote(pageIndex, bounds, text, colorHex, DateTimeOffset.UtcNow);
        await _annotationService.AddAsync(_filePath, note, ct);
        _allAnnotations.Add(note);
        NotifyAnnotationCountsChanged();
        if (pageIndex == CurrentPageIndex && MatchesFilter(note))
        {
            CurrentPageAnnotations.Add(note);
        }
    }

    [RelayCommand]
    private async Task RemoveAnnotationAsync(Annotation? annotation)
    {
        if (annotation is null)
        {
            return;
        }

        var removed = await _annotationService.RemoveAsync(_filePath, annotation.Id, CancellationToken.None);
        if (!removed)
        {
            return;
        }

        _allAnnotations.RemoveAll(a => a.Id == annotation.Id);
        NotifyAnnotationCountsChanged();
        for (int i = CurrentPageAnnotations.Count - 1; i >= 0; i--)
        {
            if (CurrentPageAnnotations[i].Id == annotation.Id)
            {
                CurrentPageAnnotations.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Сохранить изменённую аннотацию (например, новый текст StickyNote или другой цвет).
    /// Персистирует через <see cref="IAnnotationService.UpdateAsync"/> и обновляет
    /// оба внутренних списка (<c>_allAnnotations</c> и <c>CurrentPageAnnotations</c>),
    /// чтобы UI немедленно отразил изменения без перезагрузки.
    /// </summary>
    [RelayCommand]
    private async Task UpdateAnnotationAsync(Annotation? annotation)
    {
        if (annotation is null)
        {
            return;
        }

        await _annotationService.UpdateAsync(_filePath, annotation, CancellationToken.None);

        for (int i = 0; i < _allAnnotations.Count; i++)
        {
            if (_allAnnotations[i].Id == annotation.Id)
            {
                _allAnnotations[i] = annotation;
                break;
            }
        }

        for (int i = 0; i < CurrentPageAnnotations.Count; i++)
        {
            if (CurrentPageAnnotations[i].Id == annotation.Id)
            {
                CurrentPageAnnotations[i] = annotation;
                break;
            }
        }
    }

    private void RefreshCurrentPageAnnotations()
    {
        CurrentPageAnnotations.Clear();
        foreach (var a in _allAnnotations.Where(x => x.PageIndex == CurrentPageIndex && MatchesFilter(x)))
        {
            CurrentPageAnnotations.Add(a);
        }
    }

    private bool MatchesFilter(Annotation a) => AnnotationFilter switch
    {
        AnnotationFilterMode.Highlights => a.Kind == AnnotationKind.Highlight,
        AnnotationFilterMode.Notes => a.Kind == AnnotationKind.StickyNote,
        AnnotationFilterMode.Freehand => a.Kind == AnnotationKind.Freehand,
        _ => true,   // All
    };

    partial void OnAnnotationFilterChanged(AnnotationFilterMode value) =>
        RefreshCurrentPageAnnotations();
}
