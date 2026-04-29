using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Foliant.Domain;

namespace Foliant.UI.Controls;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated via XAML <ui:PageSurface/> in MainWindow.xaml; analyzer does not see XAML refs.")]
internal sealed class PageSurface : FrameworkElement
{
    public static readonly DependencyProperty PageRenderProperty = DependencyProperty.Register(
        "PageRender",
        typeof(IPageRender),
        typeof(PageSurface),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnPageRenderChanged));

    private BitmapSource? _bitmap;

    public IPageRender? PageRender
    {
        get => (IPageRender?)GetValue(PageRenderProperty);
        set => SetValue(PageRenderProperty, value);
    }

    private static void OnPageRenderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((PageSurface)d).UpdateBitmap((IPageRender?)e.NewValue);
    }

    private void UpdateBitmap(IPageRender? render)
    {
        if (render is null)
        {
            _bitmap = null;
            InvalidateVisual();
            return;
        }

        var bmp = new WriteableBitmap(render.WidthPx, render.HeightPx, 96, 96, PixelFormats.Bgra32, null);
        var rect = new Int32Rect(0, 0, render.WidthPx, render.HeightPx);

        // Zero-allocation путь: если ReadOnlyMemory<byte> — обёртка над byte[],
        // достаём массив без копии. Иначе fallback на ToArray().
        if (MemoryMarshal.TryGetArray(render.Bgra32, out var segment) && segment.Array is not null)
        {
            bmp.WritePixels(rect, segment.Array, render.Stride, segment.Offset);
        }
        else
        {
            bmp.WritePixels(rect, render.Bgra32.ToArray(), render.Stride, 0);
        }

        _bitmap = bmp;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);

        if (_bitmap is null)
        {
            drawingContext.DrawRectangle(Brushes.White, null, new Rect(RenderSize));
            return;
        }

        drawingContext.DrawImage(_bitmap, new Rect(RenderSize));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_bitmap is null)
        {
            return base.MeasureOverride(availableSize);
        }

        return new Size(_bitmap.PixelWidth, _bitmap.PixelHeight);
    }
}
