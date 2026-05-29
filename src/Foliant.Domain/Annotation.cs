namespace Foliant.Domain;

public enum AnnotationKind
{
    Highlight,
    StickyNote,
    Freehand,
    Underline,
    Strikethrough,
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
}
