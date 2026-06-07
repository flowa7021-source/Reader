namespace Foliant.Domain;

/// <summary>
/// Узел структуры документа (PDF /Outlines, EPUB nav, etc.) — для импорта в пользовательские
/// закладки и для round-trip богатого outline'а (rich read/write). Хранит flat-представление с
/// глубиной: рекурсивная иерархия плющится при чтении, caller волен использовать <see cref="Depth"/>
/// для отступа в UI или префикса заголовка.
///
/// <para>«Richness»-поля (<see cref="Destination"/>, <see cref="IsBold"/>, <see cref="IsItalic"/>,
/// <see cref="Color"/>, <see cref="IsOpen"/>) опциональны и имеют дефолты, совпадающие с прежним
/// поведением (Fit-page, без стиля/цвета, развёрнут) — поэтому существующие call-site'ы (bookmark-импорт)
/// остаются валидными без правок.</para>
/// </summary>
/// <param name="PageIndex">0-based — как и в <see cref="Bookmark.PageIndex"/>. <c>-1</c>
/// если узел указывает на нестраничный destination (URI / external file) — такие узлы
/// читатель опускает в bookmark-импорте.</param>
/// <param name="Title">Заголовок узла, как в PDF /Title (UTF-16BE декодирован).</param>
/// <param name="Depth">0 для корневых, &gt;0 — глубина вложенности.</param>
/// <param name="Destination">Режим отображения целевой страницы (PDF /Dest mode). По умолчанию
/// <see cref="OutlineDestinationMode.FitPage"/> — как было до richness.</param>
/// <param name="IsBold">Жирный заголовок (PDF /F бит 2). По умолчанию <see langword="false"/>.</param>
/// <param name="IsItalic">Курсивный заголовок (PDF /F бит 1). По умолчанию <see langword="false"/>.</param>
/// <param name="Color">RGB-цвет заголовка (PDF /C), либо <see langword="null"/> — цвет по умолчанию
/// (чёрный). По умолчанию <see langword="null"/>.</param>
/// <param name="IsOpen">Развёрнут ли узел с детьми (знак PDF /Count: ≥0 — открыт, &lt;0 — свёрнут).
/// Для листьев игнорируется. По умолчанию <see langword="true"/>.</param>
public sealed record DocumentOutlineEntry(
    int PageIndex,
    string Title,
    int Depth,
    OutlineDestinationMode Destination = OutlineDestinationMode.FitPage,
    bool IsBold = false,
    bool IsItalic = false,
    OutlineColor? Color = null,
    bool IsOpen = true);

/// <summary>
/// Режим отображения целевой страницы у PDF outline-узла — как страница позиционируется/масштабируется
/// при переходе по закладке (ISO 32000-1 §12.3.2.2, explicit destination). Покрывает четыре наиболее
/// употребимых zoom-режима; экзотические (/FitR, /FitB*) при чтении сводятся к ближайшему / к
/// <see cref="FitPage"/> (MVP).
/// </summary>
public enum OutlineDestinationMode
{
    /// <summary><c>[page /Fit]</c> — вписать всю страницу в окно. Дефолт (как было до richness).</summary>
    FitPage,

    /// <summary><c>[page /FitH null]</c> — вписать по ширине, вертикальная позиция сохраняется.</summary>
    FitWidth,

    /// <summary><c>[page /FitV null]</c> — вписать по высоте, горизонтальная позиция сохраняется.</summary>
    FitHeight,

    /// <summary><c>[page /XYZ null null null]</c> — перейти на страницу, сохранив текущий зум и позицию.</summary>
    InheritZoom,
}

/// <summary>
/// RGB-цвет заголовка outline-узла (PDF /C [r g b], ISO 32000-1 Table 153). Компоненты —
/// в диапазоне 0..1 (как в PDF). Pure-data, immutable.
/// </summary>
/// <param name="Red">Красный компонент, 0..1.</param>
/// <param name="Green">Зелёный компонент, 0..1.</param>
/// <param name="Blue">Синий компонент, 0..1.</param>
public readonly record struct OutlineColor(double Red, double Green, double Blue);
