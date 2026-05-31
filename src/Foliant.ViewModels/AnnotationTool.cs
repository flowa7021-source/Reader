namespace Foliant.ViewModels;

/// <summary>
/// Активный инструмент аннотирования в палитре (Q-F16 UI). <see cref="None"/> — режим
/// просмотра/выделения (drag не создаёт аннотацию). Остальные значения соответствуют
/// <see cref="Foliant.Domain.AnnotationKind"/> и группируются View по типу взаимодействия:
/// <list type="bullet">
/// <item><b>Rubber-band rect</b> — Highlight, Underline, Strikethrough, Rectangle, Ellipse, Stamp.</item>
/// <item><b>Two-point</b> — Line, Arrow.</item>
/// <item><b>Multi-point polyline</b> — Freehand, Polygon.</item>
/// <item><b>Single-click</b> — StickyNote.</item>
/// </list>
/// </summary>
public enum AnnotationTool
{
    None,
    Highlight,
    Underline,
    Strikethrough,
    Rectangle,
    Ellipse,
    Stamp,
    Line,
    Arrow,
    Freehand,
    Polygon,
    StickyNote,
}

/// <summary>Категория мышиного взаимодействия для <see cref="AnnotationTool"/> — определяет,
/// какой commit-метод вызывает View после жеста.</summary>
public enum AnnotationToolGesture
{
    /// <summary>Не инструмент создания (None) — жест игнорируется.</summary>
    None,

    /// <summary>Прямоугольник «резиновой рамкой»: mouse-down → drag → mouse-up.</summary>
    RubberBandRect,

    /// <summary>Две точки: mouse-down (start) → mouse-up (end).</summary>
    TwoPoint,

    /// <summary>Полилиния: серия точек (drag для freehand, клики для polygon).</summary>
    MultiPoint,

    /// <summary>Один клик — точка размещения.</summary>
    SingleClick,
}
