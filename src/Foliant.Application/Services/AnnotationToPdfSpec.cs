using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Чистый маппер <see cref="Annotation"/> → <see cref="PdfAnnotationSpec"/> для встраивания в PDF
/// (Q-F17 Phase 2). Возвращает <c>null</c> для невалидных аннотаций — те же правила, что у
/// экспортёров: highlight/note требуют <see cref="Annotation.Bounds"/>, ink — ≥2 точек. Цвет hex →
/// RGB через <see cref="HexColor"/>; альфа задаётся по типу (highlight полупрозрачен). Без I/O.
/// </summary>
public static class AnnotationToPdfSpec
{
    // ≈0.4 — как highlight в Acrobat: цвет виден, текст под ним читается. Note/Ink — непрозрачны.
    private const byte HighlightAlpha = 102;
    private const byte OpaqueAlpha = 255;

    public static PdfAnnotationSpec? Map(Annotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        return annotation.Kind switch
        {
            AnnotationKind.Highlight when annotation.Bounds is { } b => Highlight(annotation, b),
            AnnotationKind.StickyNote when annotation.Bounds is { } b => Note(annotation, b),
            AnnotationKind.Freehand when annotation.InkPoints is { Count: >= 2 } pts => Ink(annotation, pts),
            AnnotationKind.Underline when annotation.Bounds is { } b => TextMarkup(annotation, b, PdfAnnotationSubtype.Underline),
            AnnotationKind.Strikethrough when annotation.Bounds is { } b => TextMarkup(annotation, b, PdfAnnotationSubtype.Strikeout),
            AnnotationKind.Rectangle when annotation.Bounds is { } b => RectShape(annotation, b, PdfAnnotationSubtype.Square),
            AnnotationKind.Ellipse when annotation.Bounds is { } b => RectShape(annotation, b, PdfAnnotationSubtype.Circle),
            AnnotationKind.Stamp when annotation.Bounds is { } b && !string.IsNullOrWhiteSpace(annotation.Text) => Stamp(annotation, b),
            // Line/Arrow/Polygon мапятся в null здесь (PDFium 146.x не умеет /L /Vertices /LE),
            // но <c>AnnotatedPdfExportService</c> отдельно прокидывает их в
            // <c>PdfPigAnnotationAppender</c> для cos-level inкрементального апдейта (Q-F17 11/11).
            // Round-trip через FDF/XFDF/JSON тоже работает (см. #75/#76).
            _ => null,
        };
    }

    // Stamp: /Subtype /Stamp + /Contents (label) + opaque rect-border appearance. Image-stamps
    // (A5b): когда указан a.ImagePath — engine рисует embedded image вместо label-text; label
    // сохраняется в /Contents для accessibility и fallback.
    private static PdfAnnotationSpec Stamp(Annotation a, AnnotationRect b) =>
        WithMetadata(a, new PdfAnnotationSpec(
            a.PageIndex, PdfAnnotationSubtype.Stamp, Rect(b), Color(a.ColorHex, OpaqueAlpha),
            Contents: a.Text,
            ImagePath: a.ImagePath));

    private static PdfAnnotationSpec RectShape(Annotation a, AnnotationRect b, PdfAnnotationSubtype subtype) =>
        WithMetadata(a, new PdfAnnotationSpec(
            a.PageIndex, subtype, Rect(b), Color(a.ColorHex, OpaqueAlpha)));

    /// <summary>Смаппить список, отбросив невалидные.</summary>
    public static IReadOnlyList<PdfAnnotationSpec> MapMany(IReadOnlyList<Annotation> annotations)
    {
        ArgumentNullException.ThrowIfNull(annotations);

        var result = new List<PdfAnnotationSpec>(annotations.Count);
        foreach (var a in annotations)
        {
            if (Map(a) is { } spec)
            {
                result.Add(spec);
            }
        }

        return result;
    }

    private static PdfAnnotationSpec Highlight(Annotation a, AnnotationRect b) =>
        WithMetadata(a, new PdfAnnotationSpec(
            a.PageIndex, PdfAnnotationSubtype.Highlight, Rect(b), Color(a.ColorHex, HighlightAlpha),
            QuadPoints: AnnotationGeometry.QuadPoints(b)));

    // Underline/Strikethrough: та же геометрия что Highlight (rect + quadpoints), но непрозрачный
    // цвет — линия рисуется поверх текста, полупрозрачность не нужна (Acrobat также делает opaque).
    private static PdfAnnotationSpec TextMarkup(Annotation a, AnnotationRect b, PdfAnnotationSubtype subtype) =>
        WithMetadata(a, new PdfAnnotationSpec(
            a.PageIndex, subtype, Rect(b), Color(a.ColorHex, OpaqueAlpha),
            QuadPoints: AnnotationGeometry.QuadPoints(b)));

    private static PdfAnnotationSpec Note(Annotation a, AnnotationRect b) =>
        WithMetadata(a, new PdfAnnotationSpec(
            a.PageIndex, PdfAnnotationSubtype.Text, Rect(b), Color(a.ColorHex, OpaqueAlpha),
            Contents: a.Text ?? string.Empty));

    private static PdfAnnotationSpec Ink(Annotation a, IReadOnlyList<AnnotationPoint> points) =>
        WithMetadata(a, new PdfAnnotationSpec(
            a.PageIndex, PdfAnnotationSubtype.Ink, InkBounds(points), Color(a.ColorHex, OpaqueAlpha),
            InkPoints: [.. points]));

    private static PdfAnnotationSpec WithMetadata(Annotation a, PdfAnnotationSpec spec) =>
        spec with
        {
            CreatedAt = a.CreatedAt,
            ModifiedAt = a.ModifiedAt,
            Author = a.Author,
            Subject = a.Subject,
        };

    private static PdfRect Rect(AnnotationRect b)
    {
        var (xll, yll, xur, yur) = AnnotationGeometry.RectCorners(b);
        return new PdfRect(xll, yll, xur, yur);
    }

    private static PdfRect InkBounds(IReadOnlyList<AnnotationPoint> points)
    {
        double minX = points[0].X, minY = points[0].Y, maxX = points[0].X, maxY = points[0].Y;
        foreach (var p in points)
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }

        return new PdfRect(minX, minY, maxX, maxY);
    }

    private static PdfRgba Color(string hex, byte alpha) =>
        HexColor.TryParse(hex, out byte r, out byte g, out byte b)
            ? new PdfRgba(r, g, b, alpha)
            : new PdfRgba(0, 0, 0, alpha);
}
