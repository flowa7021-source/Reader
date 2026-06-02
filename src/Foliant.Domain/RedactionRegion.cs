namespace Foliant.Domain;

/// <summary>
/// Одна область физического redaction'а: индекс страницы (0-based) + прямоугольник в PDF user
/// space (origin внизу-слева, Y вверх), тот же <see cref="AnnotationRect"/>, что и у аннотаций —
/// чтобы геометрия не дублировалась между фичами.
///
/// Семантика: текстовые объекты страницы, чей bbox пересекает <see cref="Rect"/>, физически
/// удаляются из контента (и из текстового слоя), а поверх области рисуется непрозрачный чёрный
/// бокс. Find-and-redact по тексту/regex и удаление изображений — follow-up (Q-F32).
/// </summary>
public sealed record RedactionRegion(int PageIndex, AnnotationRect Rect);
