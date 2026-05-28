namespace Foliant.Domain;

/// <summary>
/// Узел структуры документа (PDF /Outlines, EPUB nav, etc.) — для импорта в пользовательские
/// закладки. Хранит flat-представление с глубиной: рекурсивная иерархия плющится при чтении,
/// caller волен использовать <see cref="Depth"/> для отступа в UI или префикса заголовка.
/// </summary>
/// <param name="PageIndex">0-based — как и в <see cref="Bookmark.PageIndex"/>. <c>-1</c>
/// если узел указывает на нестраничный destination (URI / external file) — такие узлы
/// читатель опускает в bookmark-импорте.</param>
/// <param name="Title">Заголовок узла, как в PDF /Title (UTF-16BE декодирован).</param>
/// <param name="Depth">0 для корневых, &gt;0 — глубина вложенности.</param>
public sealed record DocumentOutlineEntry(int PageIndex, string Title, int Depth);
