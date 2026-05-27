namespace Foliant.Domain;

/// <summary>
/// Геометрия прямоугольной аннотации в PDF user space (origin внизу-слева, Y вверх). Единый
/// источник для XFDF/FDF-экспортёров и PDF-writer'а, чтобы вычисление углов и quadpoints
/// не дублировалось и не расходилось. Чистый, без состояния.
/// </summary>
public static class AnnotationGeometry
{
    /// <summary>Углы прямоугольника: нижний-левый и верхний-правый — <c>(xLL, yLL, xUR, yUR)</c>.</summary>
    public static (double XLL, double YLL, double XUR, double YUR) RectCorners(AnnotationRect rect)
    {
        ArgumentNullException.ThrowIfNull(rect);
        return (rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height);
    }

    /// <summary>QuadPoints в порядке PDF — top-left, top-right, bottom-left, bottom-right —
    /// как 8 значений <c>[xTL, yTL, xTR, yTR, xBL, yBL, xBR, yBR]</c>.</summary>
    public static double[] QuadPoints(AnnotationRect rect)
    {
        var (left, bottom, right, top) = RectCorners(rect);
        return [left, top, right, top, left, bottom, right, bottom];
    }
}
