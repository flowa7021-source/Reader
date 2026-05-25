namespace Foliant.Domain;

/// <summary>
/// Прямоугольник в пиксельном пространстве рендера/экрана: origin — верхний-левый угол,
/// ось Y растёт вниз (как в bitmap-рендере PDFium и в WPF <c>Canvas</c>).
/// </summary>
public readonly record struct PixelRect(double X, double Y, double Width, double Height);

/// <summary>
/// Конвертеры координат страницы между двумя пространствами:
/// <list type="bullet">
/// <item><b>PDF user space (points)</b> — origin внизу-слева, ось Y растёт вверх.
/// В этом пространстве хранятся <see cref="AnnotationRect"/> и текстовые слои (см.
/// <see cref="Annotation"/>): они не зависят от zoom/DPI.</item>
/// <item><b>Render pixels</b> — origin вверху-слева, ось Y растёт вниз. В этом
/// пространстве работают <c>WriteableBitmap</c> и WPF-оверлеи.</item>
/// </list>
/// Преобразование включает масштаб (<see cref="PixelsPerPoint"/>) и переворот оси Y
/// относительно высоты страницы.
/// </summary>
public static class PageGeometry
{
    /// <summary>72 PDF-точки = 1 дюйм; экран по умолчанию 96 DPI.</summary>
    public static double PixelsPerPoint(double zoom) => zoom * 96.0 / 72.0;

    /// <summary>Точка PDF (pt, origin внизу-слева) → пиксель рендера (origin вверху-слева).</summary>
    public static (double X, double Y) PointToPixel(double xPt, double yPt, PageSize page, double zoom)
    {
        double scale = PixelsPerPoint(zoom);
        return (xPt * scale, (page.HeightPt - yPt) * scale);
    }

    /// <summary>Пиксель рендера (origin вверху-слева) → точка PDF (pt, origin внизу-слева).</summary>
    public static (double X, double Y) PixelToPoint(double xPx, double yPx, PageSize page, double zoom)
    {
        double scale = PixelsPerPoint(zoom);
        return (xPx / scale, page.HeightPt - yPx / scale);
    }

    /// <summary>
    /// Прямоугольник в PDF-точках (<paramref name="r"/>: X/Y — нижний-левый угол, Y вверх)
    /// → пиксельный прямоугольник (X/Y — верхний-левый угол, Y вниз).
    /// </summary>
    public static PixelRect ToPixels(AnnotationRect r, PageSize page, double zoom)
    {
        ArgumentNullException.ThrowIfNull(r);
        double scale = PixelsPerPoint(zoom);
        double topPx = (page.HeightPt - (r.Y + r.Height)) * scale;
        return new PixelRect(r.X * scale, topPx, r.Width * scale, r.Height * scale);
    }

    /// <summary>
    /// Пиксельный прямоугольник (X/Y — верхний-левый угол, Y вниз)
    /// → прямоугольник в PDF-точках (X/Y — нижний-левый угол, Y вверх).
    /// </summary>
    public static AnnotationRect ToPoints(PixelRect r, PageSize page, double zoom)
    {
        double scale = PixelsPerPoint(zoom);
        double wPt = r.Width / scale;
        double hPt = r.Height / scale;
        double xPt = r.X / scale;
        double yPt = page.HeightPt - (r.Y / scale) - hPt;
        return new AnnotationRect(xPt, yPt, wPt, hPt);
    }
}
