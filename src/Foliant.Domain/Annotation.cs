namespace Foliant.Domain;

public enum AnnotationKind
{
    Highlight,
    StickyNote,
    Freehand,
}

public sealed record AnnotationRect(double X, double Y, double Width, double Height);

public sealed record AnnotationPoint(double X, double Y);

/// <summary>
/// Аннотация поверх страницы. Координаты — в PDF user space (pt), независимо от zoom.
/// Wide record: для каждого <see cref="AnnotationKind"/> часть полей не используется
/// (Highlight: только Bounds; StickyNote: Bounds + Text; Freehand: InkPoints).
/// Фабрики <see cref="Highlight"/>/<see cref="StickyNote"/>/<see cref="Freehand"/>
/// гарантируют корректную форму.
/// </summary>
public sealed record Annotation(
    Guid Id,
    int PageIndex,
    AnnotationKind Kind,
    string ColorHex,
    AnnotationRect? Bounds,
    string? Text,
    IReadOnlyList<AnnotationPoint>? InkPoints,
    DateTimeOffset CreatedAt)
{
    public static Annotation Highlight(int pageIndex, AnnotationRect bounds, string colorHex, DateTimeOffset createdAt) =>
        new(Guid.NewGuid(), pageIndex, AnnotationKind.Highlight, colorHex, bounds, null, null, createdAt);

    public static Annotation StickyNote(int pageIndex, AnnotationRect bounds, string text, string colorHex, DateTimeOffset createdAt) =>
        new(Guid.NewGuid(), pageIndex, AnnotationKind.StickyNote, colorHex, bounds, text, null, createdAt);

    public static Annotation Freehand(int pageIndex, IReadOnlyList<AnnotationPoint> points, string colorHex, DateTimeOffset createdAt) =>
        new(Guid.NewGuid(), pageIndex, AnnotationKind.Freehand, colorHex, null, null, points, createdAt);
}
