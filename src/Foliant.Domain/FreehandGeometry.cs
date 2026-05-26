namespace Foliant.Domain;

/// <summary>
/// Упрощение freehand-обводки алгоритмом Ramer–Douglas–Peucker: прорежает «сырые» точки
/// pointer-трекинга, сохраняя форму штриха в пределах <c>epsilonPt</c> (PDF-точки).
/// Чистая геометрия — WPF-слой зовёт это на pointer-up перед персистом, чтобы не хранить
/// тысячи почти-коллинеарных точек.
/// </summary>
public static class FreehandGeometry
{
    /// <summary>Упростить ломаную. ≤2 точек или <paramref name="epsilonPt"/> ≤ 0 → вход без изменений.</summary>
    public static IReadOnlyList<AnnotationPoint> Simplify(IReadOnlyList<AnnotationPoint> points, double epsilonPt)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count <= 2 || epsilonPt <= 0)
        {
            return points;
        }

        var keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;
        SimplifySegment(points, 0, points.Count - 1, epsilonPt, keep);

        var result = new List<AnnotationPoint>(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            if (keep[i])
            {
                result.Add(points[i]);
            }
        }

        return result;
    }

    private static void SimplifySegment(
        IReadOnlyList<AnnotationPoint> pts, int first, int last, double eps, bool[] keep)
    {
        if (last <= first + 1)
        {
            return;
        }

        double maxDist = -1;
        int split = -1;
        for (int i = first + 1; i < last; i++)
        {
            double d = PerpendicularDistance(pts[i], pts[first], pts[last]);
            if (d > maxDist)
            {
                maxDist = d;
                split = i;
            }
        }

        if (maxDist > eps)
        {
            keep[split] = true;
            SimplifySegment(pts, first, split, eps, keep);
            SimplifySegment(pts, split, last, eps, keep);
        }
    }

    private static double PerpendicularDistance(AnnotationPoint p, AnnotationPoint a, AnnotationPoint b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double len2 = (dx * dx) + (dy * dy);
        if (len2 == 0)
        {
            double ax = p.X - a.X;
            double ay = p.Y - a.Y;
            return Math.Sqrt((ax * ax) + (ay * ay));
        }

        double cross = Math.Abs(((p.X - a.X) * dy) - ((p.Y - a.Y) * dx));
        return cross / Math.Sqrt(len2);
    }
}
