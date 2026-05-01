using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foliant.Application.Services;
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
    private readonly IAnnotationExporter? _exporter;
    private IReadOnlyList<Annotation> _source = Array.Empty<Annotation>();

    /// <summary>Результат последнего успешного экспорта аннотаций. Пустая строка
    /// если экспортёр не задан или список пуст. UI может показать диалог сохранения
    /// файла или скопировать в буфер при изменении этого свойства.</summary>
    [ObservableProperty]
    private string _exportedText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(TotalCount))]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    private int _refreshTick;

    /// <summary>Фильтр сайдбара: какие kind'ы аннотаций показывать. Default — All.
    /// При смене значения группы перестраиваются автоматически.</summary>
    [ObservableProperty]
    private AnnotationFilterMode _filterMode = AnnotationFilterMode.All;

    /// <summary>Поисковая строка для фильтрации заметок по содержимому (case-insensitive).
    /// Пусто/whitespace → фильтр не применяется. Не-null Text аннотации должен содержать
    /// подстроку — это автоматически исключает Highlight/Freehand (у них Text=null).</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Если true, группы страниц перечисляются от последней к первой.
    /// Default false (страница 1 сверху).</summary>
    [ObservableProperty]
    private bool _sortPageDescending;

    /// <summary>Если true, аннотации внутри группы идут от самой свежей к самой старой.
    /// Default false (старые сверху, как в журнале).</summary>
    [ObservableProperty]
    private bool _sortWithinGroupNewestFirst;

    public ObservableCollection<AnnotationPageGroup> Groups { get; } = [];

    public int TotalCount => Groups.Sum(g => g.Annotations.Count);

    public bool IsEmpty => TotalCount == 0;

    public AnnotationsDocumentViewModel(
        Action<int> onJumpToPage,
        IAnnotationExporter? exporter = null)
    {
        ArgumentNullException.ThrowIfNull(onJumpToPage);
        _onJumpToPage = onJumpToPage;
        _exporter = exporter;
    }

    /// <summary>True если экспортёр зарегистрирован и есть хотя бы одна аннотация.
    /// Биндится к IsEnabled кнопки «Export» в сайдбаре.</summary>
    public bool CanExport => _exporter is not null && !IsEmpty;

    /// <summary>Перестроить группы по новому списку аннотаций. Группирует по PageIndex,
    /// сортирует группы по странице, аннотации внутри группы — по CreatedAt.
    /// Учитывает текущий <see cref="FilterMode"/>.</summary>
    public void Rebuild(IReadOnlyList<Annotation> annotations)
    {
        ArgumentNullException.ThrowIfNull(annotations);
        _source = annotations;
        RebuildGroups();
    }

    partial void OnFilterModeChanged(AnnotationFilterMode value)
    {
        RebuildGroups();
    }

    partial void OnSearchTextChanged(string value)
    {
        RebuildGroups();
    }

    partial void OnSortPageDescendingChanged(bool value)
    {
        RebuildGroups();
    }

    partial void OnSortWithinGroupNewestFirstChanged(bool value)
    {
        RebuildGroups();
    }

    private void RebuildGroups()
    {
        Groups.Clear();
        IEnumerable<Annotation> filtered = FilterMode switch
        {
            AnnotationFilterMode.Highlights => _source.Where(a => a.Kind == AnnotationKind.Highlight),
            AnnotationFilterMode.Notes => _source.Where(a => a.Kind == AnnotationKind.StickyNote),
            AnnotationFilterMode.Freehand => _source.Where(a => a.Kind == AnnotationKind.Freehand),
            _ => _source,
        };

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string needle = SearchText;
            filtered = filtered.Where(a =>
                a.Text is { } t && t.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        var grouped = filtered.GroupBy(a => a.PageIndex);
        var orderedGroups = SortPageDescending
            ? grouped.OrderByDescending(g => g.Key)
            : grouped.OrderBy(g => g.Key);

        foreach (var group in orderedGroups)
        {
            IEnumerable<Annotation> annotations = SortWithinGroupNewestFirst
                ? group.OrderByDescending(a => a.CreatedAt)
                : group.OrderBy(a => a.CreatedAt);
            Groups.Add(new AnnotationPageGroup(group.Key, [.. annotations]));
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

    /// <summary>Экспортировать все отфильтрованные (current view) аннотации через
    /// зарегистрированный <see cref="IAnnotationExporter"/>. Результат записывается в
    /// <see cref="ExportedText"/> для обработки в View (сохранение файла / clipboard).
    /// No-op если экспортёр не задан или список аннотаций пуст.</summary>
    [RelayCommand]
    private void ExportAnnotations()
    {
        if (_exporter is null)
        {
            return;
        }

        var flat = Groups.SelectMany(g => g.Annotations).ToList();
        ExportedText = flat.Count == 0 ? string.Empty : _exporter.Export(flat);
    }
}

/// <summary>Аннотации одной страницы. <c>OneBasedPageNumber</c> готов к показу в UI.</summary>
public sealed record AnnotationPageGroup(int PageIndex, IReadOnlyList<Annotation> Annotations)
{
    public int OneBasedPageNumber => PageIndex + 1;

    public int Count => Annotations.Count;
}
