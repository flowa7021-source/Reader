using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// MediaBox + /Rotate страницы PDF — данные, необходимые для перевода аннотации из «display space»
/// (origin (0,0) в нижнем-левом углу видимой страницы, сторонами размером с пост-rotate изображение)
/// в PDF user space (origin в нижнем-левом углу MediaBox). <see cref="Rotation"/> — четверти
/// (0/1/2/3 = 0°/90°/180°/270° CW), как возвращает <c>FPDFPageGetRotation</c>.
/// </summary>
public readonly record struct PdfPageBox(
    double MediaLeft,
    double MediaBottom,
    double MediaRight,
    double MediaTop,
    int Rotation)
{
    public double Width => MediaRight - MediaLeft;

    public double Height => MediaTop - MediaBottom;

    /// <summary>Каноническая «нулевая» страница: origin (0,0), без rotate. Используется как
    /// fallback, если PDFium не отдаёт MediaBox; для таких страниц transform — identity.</summary>
    public static PdfPageBox Identity(double width, double height) => new(0, 0, width, height, 0);
}

/// <summary>
/// Пер-страничный координатный transform для PDF-аннотаций (Q-F17 follow-up к
/// <c>AnnotatedPdfExportService</c>). На входе — спека в display space, на выходе — та же спека в
/// PDF user space с учётом /Rotate и сдвига MediaBox. Чистая математика, без PDFium — отдельно
/// тестируется.
///
/// /Rotate в PDF — поворот страницы CW при показе. Обратное преобразование display→user space:
/// поворот CCW на тот же угол вокруг центра + сдвиг к лево-нижнему углу MediaBox. Для axis-aligned
/// rect'ов поворот на 90°/270° меняет местами ширину и высоту — мы пересчитываем оба угла LL/UR
/// и берём min/max, что естественно покрывает swap.
/// </summary>
public static class PdfPageCoordinateTransform
{
    public static PdfAnnotationSpec ToUserSpace(PdfAnnotationSpec spec, PdfPageBox box)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return spec with
        {
            Rect = TransformRect(spec.Rect, box),
            QuadPoints = spec.QuadPoints is { Count: 8 } q ? TransformQuadPoints(q, box) : spec.QuadPoints,
            InkPoints = spec.InkPoints is { Count: > 0 } pts ? TransformInkPoints(pts, box) : spec.InkPoints,
        };
    }

    public static PdfRect TransformRect(PdfRect r, PdfPageBox box)
    {
        var (xa, ya) = TransformPoint(r.XLL, r.YLL, box);
        var (xb, yb) = TransformPoint(r.XUR, r.YUR, box);
        return new PdfRect(
            Math.Min(xa, xb), Math.Min(ya, yb),
            Math.Max(xa, xb), Math.Max(ya, yb));
    }

    public static IReadOnlyList<double> TransformQuadPoints(IReadOnlyList<double> quad, PdfPageBox box)
    {
        ArgumentNullException.ThrowIfNull(quad);
        var result = new double[quad.Count];
        for (int i = 0; i + 1 < quad.Count; i += 2)
        {
            var (xu, yu) = TransformPoint(quad[i], quad[i + 1], box);
            result[i] = xu;
            result[i + 1] = yu;
        }

        return result;
    }

    public static IReadOnlyList<AnnotationPoint> TransformInkPoints(IReadOnlyList<AnnotationPoint> pts, PdfPageBox box)
    {
        ArgumentNullException.ThrowIfNull(pts);
        var result = new AnnotationPoint[pts.Count];
        for (int i = 0; i < pts.Count; i++)
        {
            var (xu, yu) = TransformPoint(pts[i].X, pts[i].Y, box);
            result[i] = new AnnotationPoint(xu, yu);
        }

        return result;
    }

    /// <summary>Один display-space point → PDF user space (Y-up в обоих пространствах).</summary>
    public static (double X, double Y) TransformPoint(double xDisp, double yDisp, PdfPageBox box)
    {
        double pdfW = box.Width;
        double pdfH = box.Height;
        return box.Rotation switch
        {
            1 => (box.MediaLeft + yDisp, box.MediaBottom + pdfH - xDisp),
            2 => (box.MediaLeft + pdfW - xDisp, box.MediaBottom + pdfH - yDisp),
            3 => (box.MediaLeft + pdfW - yDisp, box.MediaBottom + xDisp),
            _ => (box.MediaLeft + xDisp, box.MediaBottom + yDisp),
        };
    }
}
