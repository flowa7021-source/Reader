using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Foliant.Domain;
using Foliant.ViewModels;

namespace Foliant.UI.Controls;

/// <summary>
/// Прозрачный оверлей поверх <see cref="PageSurface"/>, рисующий аннотации текущей страницы.
/// Геометрия аннотаций хранится в PDF-точках (origin внизу-слева); слой конвертирует её в
/// пиксели рендера через <see cref="PageGeometry"/>. Масштаб и размер страницы берутся из
/// самого рендера (<see cref="IPageRender.PageSize"/> и <see cref="IPageRender.WidthPx"/>),
/// а не из текущего zoom UI — иначе во время асинхронной перерисовки после смены zoom
/// оверлей рисовался бы (и создавал заметки) по рассинхронизированным координатам.
/// Двойной клик создаёт sticky-note через <see cref="CreateNoteCommand"/>.
/// </summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated via XAML <ui:AnnotationLayer/> in MainWindow.xaml.")]
internal sealed class AnnotationLayer : FrameworkElement
{
    public static readonly DependencyProperty AnnotationsProperty = DependencyProperty.Register(
        nameof(Annotations), typeof(IEnumerable<Annotation>), typeof(AnnotationLayer),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnAnnotationsChanged));

    public static readonly DependencyProperty PageRenderProperty = DependencyProperty.Register(
        nameof(PageRender), typeof(IPageRender), typeof(AnnotationLayer),
        new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SearchHighlightsProperty = DependencyProperty.Register(
        nameof(SearchHighlights), typeof(IEnumerable<AnnotationRect>), typeof(AnnotationLayer),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSearchHighlightsChanged));

    public static readonly DependencyProperty CreateNoteCommandProperty = DependencyProperty.Register(
        nameof(CreateNoteCommand), typeof(ICommand), typeof(AnnotationLayer), new PropertyMetadata(null));

    /// <summary>Активный инструмент палитры (B1b). <see cref="AnnotationTool.None"/> — drag/click
    /// не создаёт аннотацию (только существующее поведение double-click note).</summary>
    public static readonly DependencyProperty ActiveToolProperty = DependencyProperty.Register(
        nameof(ActiveTool), typeof(AnnotationTool), typeof(AnnotationLayer),
        new PropertyMetadata(AnnotationTool.None, OnActiveToolChanged));

    /// <summary>Индекс страницы, к которой относится этот layer (B1d-multipage). Single-page
    /// биндит <c>CurrentPageIndex</c>; continuous/two-page — per-page
    /// <c>RenderedPageViewModel.PageIndex</c>. Подставляется в payload commit-команд.</summary>
    public static readonly DependencyProperty PageIndexProperty = DependencyProperty.Register(
        nameof(PageIndex), typeof(int), typeof(AnnotationLayer), new PropertyMetadata(0));

    private static void OnActiveToolChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Смена инструмента (в т.ч. Esc → ClearTool) прерывает незавершённый polygon.
        if (d is AnnotationLayer layer && layer._polygonActive)
        {
            layer.CancelPolygon();
        }
    }

    /// <summary>ICommand, вызывается с PDF-space <see cref="AnnotationRect"/> по завершении
    /// rubber-band-rect жеста (Highlight/Underline/Strikethrough/Rectangle/Ellipse/Stamp).</summary>
    public static readonly DependencyProperty CommitRectToolCommandProperty = DependencyProperty.Register(
        nameof(CommitRectToolCommand), typeof(ICommand), typeof(AnnotationLayer), new PropertyMetadata(null));

    /// <summary>ICommand, вызывается с PDF-space <see cref="AnnotationPoint"/> по single-click
    /// (StickyNote).</summary>
    public static readonly DependencyProperty CommitPointToolCommandProperty = DependencyProperty.Register(
        nameof(CommitPointToolCommand), typeof(ICommand), typeof(AnnotationLayer), new PropertyMetadata(null));

    /// <summary>ICommand, вызывается с <c>TwoPointPayload</c> по завершении two-point жеста
    /// (Line / Arrow).</summary>
    public static readonly DependencyProperty CommitTwoPointToolCommandProperty = DependencyProperty.Register(
        nameof(CommitTwoPointToolCommand), typeof(ICommand), typeof(AnnotationLayer), new PropertyMetadata(null));

    /// <summary>ICommand, вызывается с <c>IReadOnlyList&lt;AnnotationPoint&gt;</c> по завершении
    /// multi-point жеста (Freehand).</summary>
    public static readonly DependencyProperty CommitMultiPointToolCommandProperty = DependencyProperty.Register(
        nameof(CommitMultiPointToolCommand), typeof(ICommand), typeof(AnnotationLayer), new PropertyMetadata(null));

    private static readonly SolidColorBrush SearchHighlightBrush = CreateSearchHighlightBrush();

    // Минимальный шаг сэмплирования freehand-точек (в пикселях) — чтобы не копить тысячи точек.
    private const double FreehandMinStepPx = 3.0;

    // Live drag state (B1b/B1c). _dragStartPx in element-local pixels; _activeGesture фиксирует
    // тип жеста на mouse-down, чтобы up/move обрабатывали один и тот же сценарий.
    private bool _dragging;
    private AnnotationToolGesture _activeGesture;
    private Point _dragStartPx;
    private Point _dragCurrentPx;
    private readonly List<Point> _freehandPx = [];

    // Polygon click-to-add-vertex state (B1c-poly): накапливает вершины по одиночным кликам,
    // закрывается двойным кликом (или Enter); Esc/смена инструмента — отмена. _polygonHoverPx —
    // позиция курсора для «резиновой» линии от последней вершины.
    private readonly List<Point> _polygonPx = [];
    private Point _polygonHoverPx;
    private bool _polygonActive;

    public IEnumerable<Annotation>? Annotations
    {
        get => (IEnumerable<Annotation>?)GetValue(AnnotationsProperty);
        set => SetValue(AnnotationsProperty, value);
    }

    /// <summary>Прямоугольники (PDF-точки) подсветки поиска на текущей странице.</summary>
    public IEnumerable<AnnotationRect>? SearchHighlights
    {
        get => (IEnumerable<AnnotationRect>?)GetValue(SearchHighlightsProperty);
        set => SetValue(SearchHighlightsProperty, value);
    }

    public IPageRender? PageRender
    {
        get => (IPageRender?)GetValue(PageRenderProperty);
        set => SetValue(PageRenderProperty, value);
    }

    public ICommand? CreateNoteCommand
    {
        get => (ICommand?)GetValue(CreateNoteCommandProperty);
        set => SetValue(CreateNoteCommandProperty, value);
    }

    public AnnotationTool ActiveTool
    {
        get => (AnnotationTool)GetValue(ActiveToolProperty);
        set => SetValue(ActiveToolProperty, value);
    }

    public int PageIndex
    {
        get => (int)GetValue(PageIndexProperty);
        set => SetValue(PageIndexProperty, value);
    }

    public ICommand? CommitRectToolCommand
    {
        get => (ICommand?)GetValue(CommitRectToolCommandProperty);
        set => SetValue(CommitRectToolCommandProperty, value);
    }

    public ICommand? CommitPointToolCommand
    {
        get => (ICommand?)GetValue(CommitPointToolCommandProperty);
        set => SetValue(CommitPointToolCommandProperty, value);
    }

    public ICommand? CommitTwoPointToolCommand
    {
        get => (ICommand?)GetValue(CommitTwoPointToolCommandProperty);
        set => SetValue(CommitTwoPointToolCommandProperty, value);
    }

    public ICommand? CommitMultiPointToolCommand
    {
        get => (ICommand?)GetValue(CommitMultiPointToolCommandProperty);
        set => SetValue(CommitMultiPointToolCommandProperty, value);
    }

    private static void OnAnnotationsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var layer = (AnnotationLayer)d;
        if (e.OldValue is INotifyCollectionChanged oldCol)
        {
            oldCol.CollectionChanged -= layer.OnCollectionChanged;
        }

        if (e.NewValue is INotifyCollectionChanged newCol)
        {
            newCol.CollectionChanged += layer.OnCollectionChanged;
        }
    }

    private static void OnSearchHighlightsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var layer = (AnnotationLayer)d;
        if (e.OldValue is INotifyCollectionChanged oldCol)
        {
            oldCol.CollectionChanged -= layer.OnCollectionChanged;
        }

        if (e.NewValue is INotifyCollectionChanged newCol)
        {
            newCol.CollectionChanged += layer.OnCollectionChanged;
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    private static SolidColorBrush CreateSearchHighlightBrush()
    {
        var brush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xD5, 0x4F)); // translucent amber
        brush.Freeze();
        return brush;
    }

    protected override Size MeasureOverride(Size availableSize) =>
        PageRender is { } r ? new Size(r.WidthPx, r.HeightPx) : base.MeasureOverride(availableSize);

    protected override void OnRender(DrawingContext dc)
    {
        ArgumentNullException.ThrowIfNull(dc);
        if (PageRender is not { } render)
        {
            return;
        }

        // Transparent fill makes the whole overlay hit-testable (for the double-click handler).
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        PageSize page = render.PageSize;
        double zoom = EffectiveZoom(render);

        DrawSearchHighlights(dc, page, zoom);

        if (Annotations is not null)
        {
            foreach (Annotation a in Annotations)
            {
                Draw(dc, a, page, zoom);
            }
        }

        if (_dragging)
        {
            DrawPreview(dc);
        }

        if (_polygonActive && _polygonPx.Count > 0)
        {
            DrawPolygonPreview(dc);
        }
    }

    private void DrawPolygonPreview(DrawingContext dc)
    {
        var pen = new Pen(Brushes.DodgerBlue, 1) { DashStyle = DashStyles.Dash };
        for (int i = 1; i < _polygonPx.Count; i++)
        {
            dc.DrawLine(pen, _polygonPx[i - 1], _polygonPx[i]);
        }

        // Rubber-band-сегмент от последней вершины к курсору.
        dc.DrawLine(pen, _polygonPx[^1], _polygonHoverPx);

        // Маркеры вершин.
        foreach (Point v in _polygonPx)
        {
            dc.DrawEllipse(Brushes.DodgerBlue, null, v, 2.5, 2.5);
        }
    }

    private void DrawPreview(DrawingContext dc)
    {
        var pen = new Pen(Brushes.DodgerBlue, 1) { DashStyle = DashStyles.Dash };
        switch (_activeGesture)
        {
            case AnnotationToolGesture.RubberBandRect:
                dc.DrawRectangle(
                    new SolidColorBrush(Color.FromArgb(0x20, 0x1E, 0x90, 0xFF)),
                    pen,
                    new Rect(_dragStartPx, _dragCurrentPx));
                break;
            case AnnotationToolGesture.TwoPoint:
                dc.DrawLine(pen, _dragStartPx, _dragCurrentPx);
                break;
            case AnnotationToolGesture.MultiPoint:
                for (int i = 1; i < _freehandPx.Count; i++)
                {
                    dc.DrawLine(pen, _freehandPx[i - 1], _freehandPx[i]);
                }
                break;
            default:
                break;
        }
    }

    private void DrawSearchHighlights(DrawingContext dc, PageSize page, double zoom)
    {
        if (SearchHighlights is not { } highlights)
        {
            return;
        }

        foreach (AnnotationRect rect in highlights)
        {
            dc.DrawRectangle(SearchHighlightBrush, null, ToRect(rect, page, zoom));
        }
    }

    private static void Draw(DrawingContext dc, Annotation a, PageSize page, double zoom)
    {
        Color color = ParseColor(a.ColorHex);
        switch (a.Kind)
        {
            case AnnotationKind.Highlight when a.Bounds is { } b:
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x60, color.R, color.G, color.B)), null, ToRect(b, page, zoom));
                break;
            case AnnotationKind.Underline when a.Bounds is { } b:
                DrawUnderline(dc, ToRect(b, page, zoom), color);
                break;
            case AnnotationKind.Strikethrough when a.Bounds is { } b:
                DrawStrikethrough(dc, ToRect(b, page, zoom), color);
                break;
            case AnnotationKind.StickyNote when a.Bounds is { } b:
                DrawNote(dc, ToRect(b, page, zoom), color);
                break;
            case AnnotationKind.Freehand when a.InkPoints is { Count: > 1 }:
                DrawInk(dc, a.InkPoints, page, zoom, color);
                break;
            case AnnotationKind.Rectangle when a.Bounds is { } b:
                DrawRectangleShape(dc, ToRect(b, page, zoom), color);
                break;
            case AnnotationKind.Ellipse when a.Bounds is { } b:
                DrawEllipseShape(dc, ToRect(b, page, zoom), color);
                break;
            case AnnotationKind.Line when a.InkPoints is { Count: 2 }:
                DrawLineShape(dc, a.InkPoints, page, zoom, color);
                break;
            case AnnotationKind.Arrow when a.InkPoints is { Count: 2 }:
                DrawArrowShape(dc, a.InkPoints, page, zoom, color);
                break;
            case AnnotationKind.Polygon when a.InkPoints is { Count: >= 3 }:
                DrawPolygonShape(dc, a.InkPoints, page, zoom, color);
                break;
            case AnnotationKind.Stamp when a.Bounds is { } b:
                DrawStamp(dc, ToRect(b, page, zoom), color, a.Text);
                break;
            default:
                break;
        }
    }

    /// <summary>Stamp: bordered rect with centered uppercase label (typewriter feel — Approved /
    /// Rejected / Draft / custom). Label is whatever <see cref="Annotation.Text"/> carries;
    /// empty label degrades to a plain bordered rect.</summary>
    private static void DrawStamp(DrawingContext dc, System.Windows.Rect r, Color color, string? label)
    {
        var brush = new SolidColorBrush(color);
        var pen = new Pen(brush, 3) { LineJoin = PenLineJoin.Round };
        dc.DrawRectangle(null, pen, r);

        if (string.IsNullOrWhiteSpace(label))
        {
            return;
        }

        // Font size auto-fits to ~60% of rect height, clamped 8..72 px.
        double fontSize = Math.Clamp(r.Height * 0.6, 8.0, 72.0);
        var typeface = new Typeface(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var formatted = new FormattedText(
            label.ToUpperInvariant(),
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            brush,
            VisualTreeHelper.GetDpi(new System.Windows.Controls.Border()).PixelsPerDip)
        {
            TextAlignment = TextAlignment.Center,
            MaxTextWidth = Math.Max(r.Width - 8, 1),
        };
        // Vertical-center the formatted block within the rect.
        double textY = r.Top + ((r.Height - formatted.Height) / 2.0);
        dc.DrawText(formatted, new Point(r.Left, textY));
    }

    /// <summary>Rectangle (contour, no fill).</summary>
    private static void DrawRectangleShape(DrawingContext dc, Rect r, Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 2);
        dc.DrawRectangle(null, pen, r);
    }

    /// <summary>Ellipse inscribed in <paramref name="r"/>.</summary>
    private static void DrawEllipseShape(DrawingContext dc, Rect r, Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 2);
        dc.DrawEllipse(null, pen, new Point(r.Left + (r.Width / 2.0), r.Top + (r.Height / 2.0)), r.Width / 2.0, r.Height / 2.0);
    }

    /// <summary>Straight line between two points.</summary>
    private static void DrawLineShape(DrawingContext dc, IReadOnlyList<AnnotationPoint> points, PageSize page, double zoom, Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 2);
        dc.DrawLine(pen, ToPoint(points[0], page, zoom), ToPoint(points[1], page, zoom));
    }

    /// <summary>Arrow = line with arrowhead at end point. Arrowhead is two short lines at ±30°
    /// from the line direction, length proportional to line length but capped at 16 px.</summary>
    private static void DrawArrowShape(DrawingContext dc, IReadOnlyList<AnnotationPoint> points, PageSize page, double zoom, Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 2);
        Point start = ToPoint(points[0], page, zoom);
        Point end = ToPoint(points[1], page, zoom);
        dc.DrawLine(pen, start, end);

        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double len = Math.Sqrt((dx * dx) + (dy * dy));
        if (len < 1e-6)
        {
            return;
        }
        double headLen = Math.Min(16.0, len * 0.2);
        double angle = Math.Atan2(dy, dx);
        const double headHalfAngle = Math.PI / 6.0; // 30°
        var leftBarb = new Point(
            end.X - (headLen * Math.Cos(angle - headHalfAngle)),
            end.Y - (headLen * Math.Sin(angle - headHalfAngle)));
        var rightBarb = new Point(
            end.X - (headLen * Math.Cos(angle + headHalfAngle)),
            end.Y - (headLen * Math.Sin(angle + headHalfAngle)));
        dc.DrawLine(pen, end, leftBarb);
        dc.DrawLine(pen, end, rightBarb);
    }

    /// <summary>Closed polygon by vertices. Drawn as outline (no fill).</summary>
    private static void DrawPolygonShape(DrawingContext dc, IReadOnlyList<AnnotationPoint> points, PageSize page, double zoom, Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 2) { LineJoin = PenLineJoin.Round };
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(ToPoint(points[0], page, zoom), isFilled: false, isClosed: true);
            for (int i = 1; i < points.Count; i++)
            {
                ctx.LineTo(ToPoint(points[i], page, zoom), isStroked: true, isSmoothJoin: true);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private static void DrawNote(DrawingContext dc, Rect r, Color color)
    {
        var brush = new SolidColorBrush(color);
        var pen = new Pen(Brushes.DimGray, 1);
        dc.DrawRectangle(brush, pen, r);
    }

    /// <summary>Underline: тонкая линия по нижней границе bounds. Толщина — пропорциональна
    /// высоте строки, минимум 1 px, чтобы не пропадала на больших zoom-out.</summary>
    private static void DrawUnderline(DrawingContext dc, Rect r, Color color)
    {
        double thickness = Math.Max(1.0, r.Height * 0.06);
        var pen = new Pen(new SolidColorBrush(color), thickness);
        var p1 = new Point(r.Left, r.Bottom);
        var p2 = new Point(r.Right, r.Bottom);
        dc.DrawLine(pen, p1, p2);
    }

    /// <summary>Strikethrough: тонкая линия через вертикальный центр bounds.</summary>
    private static void DrawStrikethrough(DrawingContext dc, Rect r, Color color)
    {
        double thickness = Math.Max(1.0, r.Height * 0.06);
        var pen = new Pen(new SolidColorBrush(color), thickness);
        double midY = r.Top + (r.Height / 2.0);
        var p1 = new Point(r.Left, midY);
        var p2 = new Point(r.Right, midY);
        dc.DrawLine(pen, p1, p2);
    }

    private static void DrawInk(DrawingContext dc, IReadOnlyList<AnnotationPoint> points, PageSize page, double zoom, Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 2) { LineJoin = PenLineJoin.Round };
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(ToPoint(points[0], page, zoom), isFilled: false, isClosed: false);
            for (int i = 1; i < points.Count; i++)
            {
                ctx.LineTo(ToPoint(points[i], page, zoom), isStroked: true, isSmoothJoin: true);
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private static Rect ToRect(AnnotationRect b, PageSize page, double zoom)
    {
        PixelRect p = PageGeometry.ToPixels(b, page, zoom);
        return new Rect(p.X, p.Y, p.Width, p.Height);
    }

    private static Point ToPoint(AnnotationPoint pt, PageSize page, double zoom)
    {
        (double x, double y) = PageGeometry.PointToPixel(pt.X, pt.Y, page, zoom);
        return new Point(x, y);
    }

    /// <summary>
    /// Zoom, эквивалентный фактическому масштабу рендера: подбираем такое значение, чтобы
    /// <see cref="PageGeometry.PixelsPerPoint"/> совпал с <c>WidthPx / PageSize.WidthPt</c>.
    /// </summary>
    private static double EffectiveZoom(IPageRender render)
    {
        double widthPt = render.PageSize.WidthPt;
        if (widthPt <= 0)
        {
            return 1.0;
        }
        double pixelsPerPoint = render.WidthPx / widthPt;
        return pixelsPerPoint * 72.0 / 96.0;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonDown(e);

        if (PageRender is not { } render)
        {
            return;
        }

        AnnotationToolGesture gesture = DocumentTabViewModel.GestureFor(ActiveTool);

        // Polygon (MultiPoint gesture, но click-based, не press-drag): одиночный клик добавляет
        // вершину; двойной клик закрывает и коммитит. Обрабатывается до press-drag-блока.
        if (ActiveTool == AnnotationTool.Polygon)
        {
            Point click = e.GetPosition(this);
            if (e.ClickCount == 2)
            {
                CommitPolygon(render);
            }
            else
            {
                _polygonActive = true;
                _polygonPx.Add(click);
                _polygonHoverPx = click;
                InvalidateVisual();
            }
            e.Handled = true;
            return;
        }

        // Active palette tool wins over the default double-click-note behaviour. RubberBandRect,
        // TwoPoint и MultiPoint(Freehand) — все press-drag-release, разнятся только commit'ом.
        if (gesture is AnnotationToolGesture.RubberBandRect
            or AnnotationToolGesture.TwoPoint
            or AnnotationToolGesture.MultiPoint)
        {
            _dragging = true;
            _activeGesture = gesture;
            _dragStartPx = e.GetPosition(this);
            _dragCurrentPx = _dragStartPx;
            _freehandPx.Clear();
            _freehandPx.Add(_dragStartPx);
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (gesture == AnnotationToolGesture.SingleClick && CommitPointToolCommand is not null)
        {
            AnnotationPoint at = ToPdfPoint(e.GetPosition(this), render);
            var payload = new PointOnPagePayload(PageIndex, at);
            if (CommitPointToolCommand.CanExecute(payload))
            {
                CommitPointToolCommand.Execute(payload);
            }
            e.Handled = true;
            return;
        }

        // Default: double-click creates a sticky note (no active rect/click tool).
        if (e.ClickCount == 2 && CreateNoteCommand is not null)
        {
            AnnotationPoint location = ToPdfPoint(e.GetPosition(this), render);
            if (CreateNoteCommand.CanExecute(location))
            {
                CreateNoteCommand.Execute(location);
                e.Handled = true;
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);

        if (_polygonActive)
        {
            _polygonHoverPx = e.GetPosition(this);
            InvalidateVisual();
            return;
        }

        if (!_dragging)
        {
            return;
        }

        _dragCurrentPx = e.GetPosition(this);
        if (_activeGesture == AnnotationToolGesture.MultiPoint)
        {
            // Сэмплируем freehand-точки только при заметном смещении — чтобы список не разрастался.
            Point last = _freehandPx[^1];
            if (Math.Abs(_dragCurrentPx.X - last.X) >= FreehandMinStepPx
                || Math.Abs(_dragCurrentPx.Y - last.Y) >= FreehandMinStepPx)
            {
                _freehandPx.Add(_dragCurrentPx);
            }
        }

        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonUp(e);
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleaseMouseCapture();
        _dragCurrentPx = e.GetPosition(this);
        AnnotationToolGesture gesture = _activeGesture;
        InvalidateVisual();

        if (PageRender is not { } render)
        {
            return;
        }

        switch (gesture)
        {
            case AnnotationToolGesture.RubberBandRect:
                CommitRect(render);
                break;
            case AnnotationToolGesture.TwoPoint:
                CommitTwoPoint(render);
                break;
            case AnnotationToolGesture.MultiPoint:
                CommitFreehand(render);
                break;
            default:
                break;
        }
        e.Handled = true;
    }

    private void CommitRect(IPageRender render)
    {
        if (CommitRectToolCommand is null)
        {
            return;
        }

        AnnotationPoint a = ToPdfPoint(_dragStartPx, render);
        AnnotationPoint b = ToPdfPoint(_dragCurrentPx, render);
        AnnotationRect rect = PageGeometry.RectFromPoints(a, b);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return; // degenerate — ignore click-without-drag
        }

        var payload = new RectOnPagePayload(PageIndex, rect);
        if (CommitRectToolCommand.CanExecute(payload))
        {
            CommitRectToolCommand.Execute(payload);
        }
    }

    private void CommitTwoPoint(IPageRender render)
    {
        if (CommitTwoPointToolCommand is null)
        {
            return;
        }

        AnnotationPoint start = ToPdfPoint(_dragStartPx, render);
        AnnotationPoint end = ToPdfPoint(_dragCurrentPx, render);
        if (start.X == end.X && start.Y == end.Y)
        {
            return; // no drag
        }

        var payload = new TwoPointOnPagePayload(PageIndex, start, end);
        if (CommitTwoPointToolCommand.CanExecute(payload))
        {
            CommitTwoPointToolCommand.Execute(payload);
        }
    }

    private void CommitFreehand(IPageRender render)
    {
        if (CommitMultiPointToolCommand is null || _freehandPx.Count < 2)
        {
            return;
        }

        var points = new List<AnnotationPoint>(_freehandPx.Count);
        foreach (Point p in _freehandPx)
        {
            points.Add(ToPdfPoint(p, render));
        }

        var payload = new MultiPointOnPagePayload(PageIndex, points);
        if (CommitMultiPointToolCommand.CanExecute(payload))
        {
            CommitMultiPointToolCommand.Execute(payload);
        }
    }

    private void CommitPolygon(IPageRender render)
    {
        // Двойной клик закрывает полигон. Нужно ≥3 вершины (доменная фабрика Polygon требует это).
        // Последний клик двойного — это та же точка, что уже добавлена первым кликом double-click'а,
        // поэтому отдельную вершину для второго клика не добавляем.
        try
        {
            if (CommitMultiPointToolCommand is not null && _polygonPx.Count >= 3)
            {
                var points = new List<AnnotationPoint>(_polygonPx.Count);
                foreach (Point p in _polygonPx)
                {
                    points.Add(ToPdfPoint(p, render));
                }

                var payload = new MultiPointOnPagePayload(PageIndex, points);
                if (CommitMultiPointToolCommand.CanExecute(payload))
                {
                    CommitMultiPointToolCommand.Execute(payload);
                }
            }
        }
        finally
        {
            CancelPolygon();
        }
    }

    private void CancelPolygon()
    {
        _polygonActive = false;
        _polygonPx.Clear();
        InvalidateVisual();
    }

    private static AnnotationPoint ToPdfPoint(Point px, IPageRender render)
    {
        (double xPt, double yPt) = PageGeometry.PixelToPoint(px.X, px.Y, render.PageSize, EffectiveZoom(render));
        return new AnnotationPoint(xPt, yPt);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Malformed ColorHex must not crash rendering; fall back to a default colour.")]
    private static Color ParseColor(string hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch (Exception)
        {
            return Colors.Gold;
        }
    }
}
