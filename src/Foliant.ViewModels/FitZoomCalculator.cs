using Foliant.Domain;

namespace Foliant.ViewModels;

/// <summary>Чистый расчёт масштаба для режимов подгонки (<see cref="FitMode"/>).</summary>
public static class FitZoomCalculator
{
    /// <summary>
    /// Масштаб (zoom) для подгонки страницы под область просмотра, либо <see langword="null"/>,
    /// если подгонка неприменима (<see cref="FitMode.ActualSize"/> или некорректные размеры) —
    /// тогда вызывающий оставляет текущий масштаб. Результат зажимается в [minZoom, maxZoom].
    /// </summary>
    public static double? Compute(
        FitMode mode,
        PageSize page,
        double viewportWidthPx,
        double viewportHeightPx,
        double minZoom,
        double maxZoom)
    {
        if (mode == FitMode.ActualSize || page.WidthPt <= 0 || page.HeightPt <= 0 || viewportWidthPx <= 0)
        {
            return null;
        }

        double pxPerPt = PageGeometry.PixelsPerPoint(1.0);
        double zoom = viewportWidthPx / (page.WidthPt * pxPerPt);
        if (mode == FitMode.FitPage && viewportHeightPx > 0)
        {
            zoom = Math.Min(zoom, viewportHeightPx / (page.HeightPt * pxPerPt));
        }

        return Math.Clamp(zoom, minZoom, maxZoom);
    }
}
