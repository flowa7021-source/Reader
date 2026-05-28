namespace Foliant.Domain;

/// <summary>
/// Закладка пользователя на конкретной странице. Per-document. Перемещение
/// страниц внутри документа (S11) ломает закладки — это нормально и ожидаемо
/// (закладка пишется по абсолютному <see cref="PageIndex"/>, не по содержимому).
///
/// <see cref="Depth"/> — уровень вложенности в выводе (0 = root). Используется
/// импортом PDF /Outlines, чтобы сохранить структуру TOC, и Markdown-экспортом
/// для отступа в выводе. По умолчанию <c>0</c> — обычные toggle-закладки
/// (Ctrl+D) всегда плоские.
/// </summary>
public sealed record Bookmark(
    Guid Id,
    int PageIndex,
    string Label,
    DateTimeOffset CreatedAt,
    int Depth = 0)
{
    public static Bookmark Create(int pageIndex, string label, DateTimeOffset createdAt, int depth = 0) =>
        new(Guid.NewGuid(), pageIndex, label, createdAt, depth);
}
