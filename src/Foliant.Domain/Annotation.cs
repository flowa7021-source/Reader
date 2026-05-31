namespace Foliant.Domain;

public enum AnnotationKind
{
    Highlight,
    StickyNote,
    Freehand,
    Underline,
    Strikethrough,
    Rectangle,
    Ellipse,
    Line,
    Arrow,
    Polygon,
    Stamp,
}

public sealed record AnnotationRect(double X, double Y, double Width, double Height);

public sealed record AnnotationPoint(double X, double Y);

/// <summary>
/// Аннотация поверх страницы. Координаты — в PDF user space (pt), независимо от zoom.
/// Wide record: для каждого <see cref="AnnotationKind"/> часть полей не используется
/// (Highlight: только Bounds; StickyNote: Bounds + Text; Freehand: InkPoints).
/// Фабрики <see cref="Highlight"/>/<see cref="StickyNote"/>/<see cref="Freehand"/>
/// гарантируют корректную форму.
///
/// Метаданные (<see cref="ModifiedAt"/>/<see cref="Author"/>/<see cref="Subject"/>) опциональны
/// и round-trip'ятся через все форматы обмена (PDF /M /T /Subj, XFDF date/name/subject,
/// FDF /M /T /Subj, JSON). По умолчанию <c>null</c> — фабрики не требуют их менять.
/// </summary>
public sealed record Annotation(
    Guid Id,
    int PageIndex,
    AnnotationKind Kind,
    string ColorHex,
    AnnotationRect? Bounds,
    string? Text,
    IReadOnlyList<AnnotationPoint>? InkPoints,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt = null,
    string? Author = null,
    string? Subject = null)
{
    public static Annotation Highlight(int pageIndex, AnnotationRect bounds, string colorHex, DateTimeOffset createdAt) =>
        new(Guid.NewGuid(), pageIndex, AnnotationKind.Highlight, colorHex, bounds, null, null, createdAt);

    public static Annotation StickyNote(int pageIndex, AnnotationRect bounds, string text, string colorHex, DateTimeOffset createdAt) =>
        new(Guid.NewGuid(), pageIndex, AnnotationKind.StickyNote, colorHex, bounds, text, null, createdAt);

    public static Annotation Freehand(int pageIndex, IReadOnlyList<AnnotationPoint> points, string colorHex, DateTimeOffset createdAt) =>
        new(Guid.NewGuid(), pageIndex, AnnotationKind.Freehand, colorHex, null, null, points, createdAt);

    public static Annotation Underline(int pageIndex, AnnotationRect bounds, string colorHex, DateTimeOffset createdAt) =>
        new(Guid.NewGuid(), pageIndex, AnnotationKind.Underline, colorHex, bounds, null, null, createdAt);

    public static Annotation Strikethrough(int pageIndex, AnnotationRect bounds, string colorHex, DateTimeOffset createdAt) =>
        new(Guid.NewGuid(), pageIndex, AnnotationKind.Strikethrough, colorHex, bounds, null, null, createdAt);

    /// <summary>Прямоугольник: <paramref name="bounds"/> = bbox; контур (без заливки) рисуется
    /// по периметру.</summary>
    public static Annotation Rectangle(int pageIndex, AnnotationRect bounds, string colorHex, DateTimeOffset createdAt) =>
        new(Guid.NewGuid(), pageIndex, AnnotationKind.Rectangle, colorHex, bounds, null, null, createdAt);

    /// <summary>Эллипс, вписанный в <paramref name="bounds"/>.</summary>
    public static Annotation Ellipse(int pageIndex, AnnotationRect bounds, string colorHex, DateTimeOffset createdAt) =>
        new(Guid.NewGuid(), pageIndex, AnnotationKind.Ellipse, colorHex, bounds, null, null, createdAt);

    /// <summary>Прямая линия между двумя точками. <paramref name="points"/> должен содержать
    /// ровно две точки (start, end).</summary>
    public static Annotation Line(int pageIndex, IReadOnlyList<AnnotationPoint> points, string colorHex, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count != 2)
        {
            throw new ArgumentException("Line requires exactly two points.", nameof(points));
        }
        return new(Guid.NewGuid(), pageIndex, AnnotationKind.Line, colorHex, null, null, points, createdAt);
    }

    /// <summary>Стрелка между двумя точками: рисуется как линия + arrowhead на конце.
    /// <paramref name="points"/> должен содержать ровно две точки (start, end).</summary>
    public static Annotation Arrow(int pageIndex, IReadOnlyList<AnnotationPoint> points, string colorHex, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count != 2)
        {
            throw new ArgumentException("Arrow requires exactly two points.", nameof(points));
        }
        return new(Guid.NewGuid(), pageIndex, AnnotationKind.Arrow, colorHex, null, null, points, createdAt);
    }

    /// <summary>Полигон по списку вершин (≥3). Замыкается автоматически при отрисовке.</summary>
    public static Annotation Polygon(int pageIndex, IReadOnlyList<AnnotationPoint> points, string colorHex, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count < 3)
        {
            throw new ArgumentException("Polygon requires at least three points.", nameof(points));
        }
        return new(Guid.NewGuid(), pageIndex, AnnotationKind.Polygon, colorHex, null, null, points, createdAt);
    }

    /// <summary>Штамп — bounds + label-текст (например, "APPROVED" / "REJECTED" / "DRAFT" / любая
    /// custom-строка). Рендерится как прямоугольник с обводкой в <paramref name="colorHex"/>
    /// и центрированной надписью. Image-stamps (PNG → stamp) — следующий PR.</summary>
    public static Annotation Stamp(int pageIndex, AnnotationRect bounds, string label, string colorHex, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(colorHex);
        return new(Guid.NewGuid(), pageIndex, AnnotationKind.Stamp, colorHex, bounds, label, null, createdAt);
    }
}
