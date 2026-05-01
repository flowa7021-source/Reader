using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foliant.Domain;

namespace Foliant.ViewModels;

/// <summary>
/// «Document → All Annotations» сайдбар: показывает все аннотации документа,
/// сгруппированные по странице (1-based номера в UI). Click в строку →
/// <c>onJumpToPage(pageIndex)</c> уводит активную страницу к выбранной.
/// </summary>
public sealed partial class AnnotationsDocumentViewModel : ObservableObject
{
    private readonly Action<int> _onJumpToPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(TotalCount))]
    private int _refreshTick;

    public ObservableCollection<AnnotationPageGroup> Groups { get; } = [];

    public int TotalCount => Groups.Sum(g => g.Annotations.Count);

    public bool IsEmpty => TotalCount == 0;

    public AnnotationsDocumentViewModel(Action<int> onJumpToPage)
    {
        ArgumentNullException.ThrowIfNull(onJumpToPage);
        _onJumpToPage = onJumpToPage;
    }

    /// <summary>Перестроить группы по новому списку аннотаций. Группирует по PageIndex,
    /// сортирует группы по странице, аннотации внутри группы — по CreatedAt.</summary>
    public void Rebuild(IReadOnlyList<Annotation> annotations)
    {
        ArgumentNullException.ThrowIfNull(annotations);
        Groups.Clear();
        foreach (var group in annotations.GroupBy(a => a.PageIndex).OrderBy(g => g.Key))
        {
            Groups.Add(new AnnotationPageGroup(
                group.Key,
                [.. group.OrderBy(a => a.CreatedAt)]));
        }
        RefreshTick++;
    }

    [RelayCommand]
    private void JumpToAnnotation(Annotation? annotation)
    {
        if (annotation is null)
        {
            return;
        }
        _onJumpToPage(annotation.PageIndex);
    }
}

/// <summary>Аннотации одной страницы. <c>OneBasedPageNumber</c> готов к показу в UI.</summary>
public sealed record AnnotationPageGroup(int PageIndex, IReadOnlyList<Annotation> Annotations)
{
    public int OneBasedPageNumber => PageIndex + 1;

    public int Count => Annotations.Count;
}
