using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foliant.Domain;

namespace Foliant.ViewModels;

/// <summary>
/// Состояние палитры инструментов аннотирования (Q-F16 UI, track B1a). Держит активный
/// инструмент + параметры (<see cref="NewAnnotationColorHex"/>, label штампа, текст заметки) и
/// диспетчеризует жест View (rect / две точки / полилиния / клик) в соответствующий
/// <c>AddXAsync</c>. View передаёт индекс страницы и уже сконвертированные PDF-координаты;
/// здесь — только маршрутизация и валидация, поэтому всё unit-тестируемо.
/// </summary>
public sealed partial class DocumentTabViewModel
{
    /// <summary>Активный инструмент. <see cref="AnnotationTool.None"/> — режим просмотра.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveToolGesture))]
    [NotifyPropertyChangedFor(nameof(IsAnnotationCreationActive))]
    private AnnotationTool _activeTool = AnnotationTool.None;

    /// <summary>Цвет (hex <c>#RRGGBB</c>) для новых аннотаций, создаваемых через палитру.</summary>
    [ObservableProperty]
    private string _newAnnotationColorHex = "#FFEB3B";

    /// <summary>Label для штампа (используется при <see cref="AnnotationTool.Stamp"/>).</summary>
    [ObservableProperty]
    private string _stampLabel = "APPROVED";

    /// <summary>Текст для новой заметки (используется при <see cref="AnnotationTool.StickyNote"/>).</summary>
    [ObservableProperty]
    private string _stickyNoteText = "Note";

    /// <summary>Размер стороны заметки в PDF-pt при single-click размещении (квадрат).</summary>
    private const double StickyNoteSizePt = 18.0;

    /// <summary>True, если активен любой инструмент создания (не None).</summary>
    public bool IsAnnotationCreationActive => ActiveTool != AnnotationTool.None;

    /// <summary>Категория жеста для активного инструмента — View выбирает по ней обработчик мыши.</summary>
    public AnnotationToolGesture ActiveToolGesture => GestureFor(ActiveTool);

    /// <summary>Категория жеста для произвольного инструмента (чистая функция).</summary>
    public static AnnotationToolGesture GestureFor(AnnotationTool tool) => tool switch
    {
        AnnotationTool.Highlight or AnnotationTool.Underline or AnnotationTool.Strikethrough
            or AnnotationTool.Rectangle or AnnotationTool.Ellipse or AnnotationTool.Stamp
            => AnnotationToolGesture.RubberBandRect,
        AnnotationTool.Line or AnnotationTool.Arrow => AnnotationToolGesture.TwoPoint,
        AnnotationTool.Freehand or AnnotationTool.Polygon => AnnotationToolGesture.MultiPoint,
        AnnotationTool.StickyNote => AnnotationToolGesture.SingleClick,
        _ => AnnotationToolGesture.None,
    };

    /// <summary>Выбрать инструмент. Повторный выбор того же инструмента сбрасывает в None
    /// (toggle-семантика для toolbar-кнопок).</summary>
    [RelayCommand]
    private void SelectTool(AnnotationTool tool) =>
        ActiveTool = ActiveTool == tool ? AnnotationTool.None : tool;

    /// <summary>Сбросить инструмент в режим просмотра (например по Esc или после создания).</summary>
    [RelayCommand]
    private void ClearTool() => ActiveTool = AnnotationTool.None;

    /// <summary>ICommand-обёртка для View: завершить rubber-band-rect жест на ТЕКУЩЕЙ странице.
    /// AnnotationLayer (single-page) передаёт сюда уже сконвертированный PDF-rect; маршрутизация
    /// и валидация — в <see cref="CommitRectToolAsync"/>.</summary>
    [RelayCommand]
    private async Task CommitRectToolOnCurrentPage(AnnotationRect? bounds)
    {
        if (bounds is not null)
        {
            await CommitRectToolAsync(CurrentPageIndex, bounds).ConfigureAwait(false);
        }
    }

    /// <summary>ICommand-обёртка: завершить single-click жест (StickyNote) на текущей странице.</summary>
    [RelayCommand]
    private async Task CommitPointToolOnCurrentPage(AnnotationPoint? at)
    {
        if (at is not null)
        {
            await CommitPointToolAsync(CurrentPageIndex, at).ConfigureAwait(false);
        }
    }

    /// <summary>ICommand-обёртка (B1c): завершить two-point жест (Line / Arrow) на текущей
    /// странице. View передаёт пару PDF-точек; валидация — в <see cref="CommitTwoPointToolAsync"/>.</summary>
    [RelayCommand]
    private async Task CommitTwoPointToolOnCurrentPage(TwoPointPayload? payload)
    {
        if (payload is not null)
        {
            await CommitTwoPointToolAsync(CurrentPageIndex, payload.Start, payload.End).ConfigureAwait(false);
        }
    }

    /// <summary>ICommand-обёртка (B1c): завершить multi-point жест (Freehand / Polygon) на
    /// текущей странице. View передаёт собранные PDF-точки; валидация (≥2 / ≥3) — в
    /// <see cref="CommitMultiPointToolAsync"/>.</summary>
    [RelayCommand]
    private async Task CommitMultiPointToolOnCurrentPage(IReadOnlyList<AnnotationPoint>? points)
    {
        if (points is not null)
        {
            await CommitMultiPointToolAsync(CurrentPageIndex, points).ConfigureAwait(false);
        }
    }

    /// <summary>Завершить rubber-band-rect жест: создать аннотацию rect-группы на странице
    /// <paramref name="pageIndex"/> по PDF-координатам <paramref name="bounds"/>. No-op для
    /// вырожденного прямоугольника, blank stamp-label или не-rect инструмента. Возвращает
    /// <c>true</c>, если аннотация была создана.</summary>
    public async Task<bool> CommitRectToolAsync(int pageIndex, AnnotationRect bounds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        if (ActiveToolGesture != AnnotationToolGesture.RubberBandRect
            || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return false;
        }

        string color = NewAnnotationColorHex;
        switch (ActiveTool)
        {
            case AnnotationTool.Highlight: await AddHighlightAsync(pageIndex, bounds, color, ct).ConfigureAwait(false); break;
            case AnnotationTool.Underline: await AddUnderlineAsync(pageIndex, bounds, color, ct).ConfigureAwait(false); break;
            case AnnotationTool.Strikethrough: await AddStrikethroughAsync(pageIndex, bounds, color, ct).ConfigureAwait(false); break;
            case AnnotationTool.Rectangle: await AddRectangleAsync(pageIndex, bounds, color, ct).ConfigureAwait(false); break;
            case AnnotationTool.Ellipse: await AddEllipseAsync(pageIndex, bounds, color, ct).ConfigureAwait(false); break;
            case AnnotationTool.Stamp:
                if (string.IsNullOrWhiteSpace(StampLabel))
                {
                    return false;
                }
                await AddStampAsync(pageIndex, bounds, StampLabel, color, ct).ConfigureAwait(false);
                break;
            default: return false;
        }

        return true;
    }

    /// <summary>Завершить two-point жест (Line / Arrow). Точки должны различаться.</summary>
    public async Task<bool> CommitTwoPointToolAsync(int pageIndex, AnnotationPoint start, AnnotationPoint end, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(end);
        if (ActiveToolGesture != AnnotationToolGesture.TwoPoint
            || (start.X == end.X && start.Y == end.Y))
        {
            return false;
        }

        AnnotationPoint[] points = [start, end];
        string color = NewAnnotationColorHex;
        switch (ActiveTool)
        {
            case AnnotationTool.Line: await AddLineAsync(pageIndex, points, color, ct).ConfigureAwait(false); break;
            case AnnotationTool.Arrow: await AddArrowAsync(pageIndex, points, color, ct).ConfigureAwait(false); break;
            default: return false;
        }

        return true;
    }

    /// <summary>Завершить multi-point жест (Freehand / Polygon). Freehand требует ≥2 точек,
    /// Polygon ≥3 (как доменные фабрики).</summary>
    public async Task<bool> CommitMultiPointToolAsync(int pageIndex, IReadOnlyList<AnnotationPoint> points, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (ActiveToolGesture != AnnotationToolGesture.MultiPoint)
        {
            return false;
        }

        string color = NewAnnotationColorHex;
        switch (ActiveTool)
        {
            case AnnotationTool.Freehand:
                if (points.Count < 2)
                {
                    return false;
                }
                await AddFreehandAsync(pageIndex, points, color, ct).ConfigureAwait(false);
                break;
            case AnnotationTool.Polygon:
                if (points.Count < 3)
                {
                    return false;
                }
                await AddPolygonAsync(pageIndex, points, color, ct).ConfigureAwait(false);
                break;
            default: return false;
        }

        return true;
    }

    /// <summary>Завершить single-click жест (StickyNote): создаёт квадратную заметку с центром
    /// в точке клика <paramref name="at"/> на странице <paramref name="pageIndex"/>.</summary>
    public async Task<bool> CommitPointToolAsync(int pageIndex, AnnotationPoint at, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(at);
        if (ActiveTool != AnnotationTool.StickyNote)
        {
            return false;
        }

        double half = StickyNoteSizePt / 2.0;
        var bounds = new AnnotationRect(at.X - half, at.Y - half, StickyNoteSizePt, StickyNoteSizePt);
        await AddNoteAsync(pageIndex, bounds, StickyNoteText, NewAnnotationColorHex, ct).ConfigureAwait(false);
        return true;
    }
}

/// <summary>View-supplied envelope для <c>CommitTwoPointToolOnCurrentPageCommand</c>: пара
/// PDF-точек (начало/конец) two-point жеста Line / Arrow.</summary>
public sealed record TwoPointPayload(AnnotationPoint Start, AnnotationPoint End);
