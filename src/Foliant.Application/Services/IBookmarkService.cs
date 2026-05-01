using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Path-based facade над <see cref="IBookmarkStore"/>. ViewModel передаёт
/// абсолютный путь документа; сервис внутри получает fingerprint и делегирует
/// в store. <see cref="ToggleAsync"/> — удобный helper для Ctrl+D-сценария:
/// если на странице уже есть закладка — удалить, иначе добавить.
/// </summary>
public interface IBookmarkService
{
    Task<IReadOnlyList<Bookmark>> ListAsync(string documentPath, CancellationToken ct);

    Task<Bookmark> AddAsync(string documentPath, int pageIndex, string label, CancellationToken ct);

    Task<bool> RemoveAsync(string documentPath, Guid bookmarkId, CancellationToken ct);

    /// <summary>Переименовать закладку. Возвращает обновлённый <see cref="Bookmark"/> или
    /// <c>null</c> если такой <paramref name="bookmarkId"/> не найден.</summary>
    Task<Bookmark?> RenameAsync(string documentPath, Guid bookmarkId, string newLabel, CancellationToken ct);

    /// <summary>Если на <paramref name="pageIndex"/> уже есть закладка — удаляет её, возвращает <c>null</c>.
    /// Иначе создаёт новую и возвращает её.</summary>
    Task<Bookmark?> ToggleAsync(string documentPath, int pageIndex, string label, CancellationToken ct);
}
