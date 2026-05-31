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
        new PropertyMetadata(AnnotationTool.None));

    /// <summary>ICommand, вызывается с PDF-space <see cref="AnnotationRect"/> по завершении
    /// rubber-band-rect жеста (Highlight/Underline/Strikethrough/Rectangle/Ellipse/Stamp).</summary>
    public static readonly DependencyProperty CommitRectToolCommandProperty = DependencyProperty.Register(
        nameof(CommitRectToolCommand), typeof(ICommand), typeof(AnnotationLayer), new PropertyMetadata(null));

    /// <summary>ICommand, вызывается с PDF-space <see cref="AnnotationPoint"/> по single-click
    /// (StickyNote).</summary>
    public static readonly DependencyProperty CommitPointToolCommandProperty = DependencyProperty.Register(
        nameof(CommitPointToolCommand), typeof(ICommand), typeof(AnnotationLayer), new PropertyMetadata(null));

    private static readonly SolidColorBrush SearchHighlightBrush = CreateSearchHighlightBrush();

    // Live rubber-band drag state (B1b). _dragStartPx in element-local pixels.
    private bool _dragging;
    private Point _dragStartPx;
    private Point _dragCurrentPx;

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
            var preview = new Rect(_dragStartPx, _dragCurrentPx);
            var pen = new Pen(Brushes.DodgerBlue, 1) { DashStyle = DashStyles.Dash };
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x20, 0x1E, 0x90, 0xFF)), pen, preview);
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

        // Active palette tool wins over the default double-click-note behaviour.
        if (gesture == AnnotationToolGesture.RubberBandRect)
        {
            _dragging = true;
            _dragStartPx = e.GetPosition(this);
            _dragCurrentPx = _dragStartPx;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (gesture == AnnotationToolGesture.SingleClick && CommitPointToolCommand is not null)
        {
            AnnotationPoint at = ToPdfPoint(e.GetPosition(this), render);
            if (CommitPointToolCommand.CanExecute(at))
            {
                CommitPointToolCommand.Execute(at);
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
        if (_dragging)
        {
            _dragCurrentPx = e.GetPosition(this);
            InvalidateVisual();
        }
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
        InvalidateVisual();

        if (PageRender is not { } render || CommitRectToolCommand is null)
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

        if (CommitRectToolCommand.CanExecute(rect))
        {
            CommitRectToolCommand.Execute(rect);
        }
        e.Handled = true;
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
