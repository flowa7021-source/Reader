using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>Подтип PDF-аннотации для записи в документ.</summary>
public enum PdfAnnotationSubtype
{
    Highlight,
    Text,
    Ink,

    /// <summary>PDF text-markup `/Subtype /Underline` — линия под текстом, та же геометрия что Highlight.</summary>
    Underline,

    /// <summary>PDF text-markup `/Subtype /StrikeOut` — линия через центр текста, та же геометрия что Highlight.</summary>
    Strikeout,

    /// <summary>PDF `/Subtype /Square` — прямоугольный контур.</summary>
    Square,

    /// <summary>PDF `/Subtype /Circle` — эллипс/круг.</summary>
    Circle,

    /// <summary>PDF `/Subtype /Stamp` — штамп с label-текстом (<c>/Contents</c>) и rect-border
    /// appearance'ом, рисуемым внутри annotation.</summary>
    Stamp,
}

/// <summary>Прямоугольник в PDF user space: нижний-левый и верхний-правый углы.</summary>
public readonly record struct PdfRect(double XLL, double YLL, double XUR, double YUR);

/// <summary>Цвет аннотации с альфой (0..255). Highlight полупрозрачен, Note/Ink непрозрачны.</summary>
public readonly record struct PdfRgba(byte R, byte G, byte B, byte A);

/// <summary>
/// Числовое, PDF-нейтральное описание одной аннотации для записи в PDF (PDFium-writer).
/// Координаты — PDF user space. Чистый результат маппинга <see cref="AnnotationToPdfSpec"/>,
/// без зависимости от движка — поэтому полностью unit-тестируемо.
/// </summary>
public sealed record PdfAnnotationSpec(
    int PageIndex,
    PdfAnnotationSubtype Subtype,
    PdfRect Rect,
    PdfRgba Color,
    IReadOnlyList<double>? QuadPoints = null,
    IReadOnlyList<AnnotationPoint>? InkPoints = null,
    string? Contents = null,
    DateTimeOffset? CreatedAt = null,
    DateTimeOffset? ModifiedAt = null,
    string? Author = null,
    string? Subject = null);
