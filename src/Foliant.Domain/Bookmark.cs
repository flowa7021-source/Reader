namespace Foliant.Domain;

/// <summary>
/// Закладка пользователя на конкретной странице. Per-document. Перемещение
/// страниц внутри документа (S11) ломает закладки — это нормально и ожидаемо
/// (закладка пишется по абсолютному <see cref="PageIndex"/>, не по содержимому).
/// </summary>
public sealed record Bookmark(
    Guid Id,
    int PageIndex,
    string Label,
    DateTimeOffset CreatedAt)
{
    public static Bookmark Create(int pageIndex, string label, DateTimeOffset createdAt) =>
        new(Guid.NewGuid(), pageIndex, label, createdAt);
}
