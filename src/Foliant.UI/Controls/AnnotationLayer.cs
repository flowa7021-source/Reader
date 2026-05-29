using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Foliant.Domain;

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

    private static readonly SolidColorBrush SearchHighlightBrush = CreateSearchHighlightBrush();

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
            default:
                break;
        }
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

        if (e.ClickCount != 2 || PageRender is not { } render || CreateNoteCommand is null)
        {
            return;
        }

        Point px = e.GetPosition(this);
        PageSize page = render.PageSize;
        (double xPt, double yPt) = PageGeometry.PixelToPoint(px.X, px.Y, page, EffectiveZoom(render));
        var location = new AnnotationPoint(xPt, yPt);
        if (CreateNoteCommand.CanExecute(location))
        {
            CreateNoteCommand.Execute(location);
            e.Handled = true;
        }
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
