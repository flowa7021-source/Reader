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
    /// <c>PropertyChanged</c> для всех зависимых count-property.</summary>
    private void NotifyAnnotationCountsChanged()
    {
        OnPropertyChanged(nameof(TotalAnnotationsCount));
        OnPropertyChanged(nameof(HighlightCount));
        OnPropertyChanged(nameof(NoteCount));
        OnPropertyChanged(nameof(FreehandCount));
        OnPropertyChanged(nameof(CanExportAnnotatedPdf));
        ExportAnnotatedPdfCommand.NotifyCanExecuteChanged();
        AnnotationsDocument.Rebuild(_allAnnotations);
        RefreshVisiblePageAnnotations();
    }

    /// <summary>Можно ли экспортировать PDF со встроенными аннотациями: задан сервис, источник —
    /// PDF и есть хотя бы одна аннотация. Биндится к доступности пункта меню «Export annotated PDF».</summary>
    public bool CanExportAnnotatedPdf =>
        _annotatedPdfExporter is not null
        && _allAnnotations.Count > 0
        && Path.GetExtension(_filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Записать копию исходного PDF со встроенными <b>редактируемыми</b> аннотациями
    /// (/Highlight, /Text, /Ink). Путь назначения выбирает View (SaveFileDialog) и передаёт сюда.
    /// No-op, если экспорт неприменим или путь пуст; сбой логируется и не роняет вкладку.</summary>
    [RelayCommand(CanExecute = nameof(CanExportAnnotatedPdf))]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Annotated-PDF export failure must not crash the tab.")]
    private async Task ExportAnnotatedPdfAsync(string? targetPath, CancellationToken ct)
    {
        if (_annotatedPdfExporter is null || string.IsNullOrWhiteSpace(targetPath) || !CanExportAnnotatedPdf)
        {
            return;
        }

        try
        {
            await _annotatedPdfExporter.ExportAsync(_filePath, [.. _allAnnotations], targetPath, ct);
        }
        catch (OperationCanceledException)
        {
            // отменено пользователем/закрытием — игнорируем
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to export annotated PDF to '{Path}'.", targetPath);
        }
    }

    /// <summary>Видимость сайдбара «All Annotations» (toggle из меню/тулбара).</summary>
    [ObservableProperty]
    private bool _isAnnotationsVisible;

    [RelayCommand]
    private void ToggleAnnotations() => IsAnnotationsVisible = !IsAnnotationsVisible;

    /// <summary>Активный инструмент палитры аннотаций (highlight/note/freehand) или null,
    /// когда ничего не выбрано. Future XAML-палитра биндится к этому property; решает,
    /// какой жест pointer-down/up создаёт (highlight-rect vs freehand-ink). Состояние
    /// живёт в VM, чтобы highlight-vs-freehand путь был тестируемым.</summary>
    [ObservableProperty]
    private AnnotationKind? _activeAnnotationTool;

    /// <summary>Выбрать инструмент палитры. Повторный выбор того же инструмента снимает
    /// выбор (toggle), что соответствует поведению toolbar-кнопок.</summary>
    [RelayCommand]
    private void SelectAnnotationTool(AnnotationKind tool) =>
        ActiveAnnotationTool = ActiveAnnotationTool == tool ? null : tool;

    /// <summary>Снять выбор инструмента (Esc / клик мимо палитры).</summary>
    [RelayCommand]
    private void ClearAnnotationTool() => ActiveAnnotationTool = null;

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

        var hl = Annotation.Highlight(pageIndex, bounds, colorHex, DateTimeOffset.UtcNow) with
        {
            Author = ReadDefaultAuthor(),
        };
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

        var note = Annotation.StickyNote(pageIndex, bounds, text, colorHex, DateTimeOffset.UtcNow) with
        {
            Author = ReadDefaultAuthor(),
        };
        await _annotationService.AddAsync(_filePath, note, ct);
        _allAnnotations.Add(note);
        NotifyAnnotationCountsChanged();
        if (pageIndex == CurrentPageIndex && MatchesFilter(note))
        {
            CurrentPageAnnotations.Add(note);
        }
    }

    private string? ReadDefaultAuthor()
    {
        string? raw = _settings?.Current.DefaultAnnotationAuthor;
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }

    /// <summary>Зафиксировать freehand-обводку — вызывается WPF-слоем на pointer-up с собранными
    /// точками (PDF user space). Персистит через сервис и обновляет counts/sidebar/visible-pages
    /// тем же путём, что add-highlight/add-note. No-op при пустом или единственном point
    /// (нет видимого штриха).</summary>
    public async Task AddFreehandAsync(int pageIndex, IReadOnlyList<AnnotationPoint> points, string colorHex, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(colorHex);

        if (points.Count < 2)
        {
            return;
        }

        var ink = Annotation.Freehand(pageIndex, [.. points], colorHex, DateTimeOffset.UtcNow) with
        {
            Author = ReadDefaultAuthor(),
        };
        await _annotationService.AddAsync(_filePath, ink, ct);
        _allAnnotations.Add(ink);
        NotifyAnnotationCountsChanged();
        if (pageIndex == CurrentPageIndex && MatchesFilter(ink))
        {
            CurrentPageAnnotations.Add(ink);
        }
    }

    /// <summary>Размер новой sticky-note по умолчанию (в PDF-точках).</summary>
    private const double DefaultNoteSizePt = 18.0;

    /// <summary>Создать sticky-note в точке (PDF user space) на ТЕКУЩЕЙ странице — вызывается
    /// двойным кликом в одностраничном режиме. Точка — верхний-левый угол заметки (PDF ось Y вверх).</summary>
    [RelayCommand]
    private Task AddNoteAtAsync(AnnotationPoint? location) => AddNoteAtPageAsync(CurrentPageIndex, location);

    /// <summary>Page-aware вариант: заметка попадает на указанную страницу, а не на
    /// <see cref="CurrentPageIndex"/>. Нужен multi-page слоям (continuous/two-page), где двойной
    /// клик происходит по слоту конкретной страницы, отличной от текущей.</summary>
    public Task AddNoteAtPageAsync(int pageIndex, AnnotationPoint? location, CancellationToken ct = default)
    {
        if (location is null)
        {
            return Task.CompletedTask;
        }

        var bounds = new AnnotationRect(location.X, location.Y - DefaultNoteSizePt, DefaultNoteSizePt, DefaultNoteSizePt);
        return AddNoteAsync(pageIndex, bounds, "Note", "#FFEB3B", ct);
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

        AnnotationsDocument.Rebuild(_allAnnotations);
        RefreshVisiblePageAnnotations();
    }

    /// <summary>Изменить текст существующей sticky-note. Композирует новую запись через
    /// <c>with</c> (Id/координаты/дата сохраняются) и персистит через тот же путь, что и
    /// <see cref="UpdateAnnotationAsync"/>. No-op если аннотация не StickyNote или текст
    /// не изменился. Параметр — кортеж (annotation, newText), удобно биндить из note-editor.</summary>
    [RelayCommand]
    private Task EditNoteTextAsync((Annotation Annotation, string Text) edit)
    {
        if (edit.Annotation is not { Kind: AnnotationKind.StickyNote } note)
        {
            return Task.CompletedTask;
        }

        ArgumentNullException.ThrowIfNull(edit.Text);
        if (string.Equals(note.Text, edit.Text, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        // Любая правка содержимого — обновляем ModifiedAt; иначе /M и FDF/XFDF date останутся
        // в момент создания, что для рецензента бесполезно.
        return UpdateAnnotationAsync(note with { Text = edit.Text, ModifiedAt = DateTimeOffset.UtcNow });
    }

    /// <summary>Изменить <see cref="Annotation.Subject"/> любой аннотации (highlight/note/ink) —
    /// тема не привязана к типу. Whitespace/null нормализуется к null (пустая тема = отсутствие).
    /// <see cref="Annotation.ModifiedAt"/> бампится — round-trip через FDF/XFDF/PDF понесёт
    /// актуальную дату. No-op, если значение не изменилось.</summary>
    [RelayCommand]
    private Task EditNoteSubjectAsync((Annotation Annotation, string? Subject) edit)
    {
        if (edit.Annotation is null)
        {
            return Task.CompletedTask;
        }

        string? normalized = string.IsNullOrWhiteSpace(edit.Subject) ? null : edit.Subject.Trim();
        if (string.Equals(edit.Annotation.Subject, normalized, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        return UpdateAnnotationAsync(edit.Annotation with { Subject = normalized, ModifiedAt = DateTimeOffset.UtcNow });
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
