using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;

namespace Foliant.Infrastructure.Bookmarks;

public sealed class BookmarkService(
    IBookmarkStore store,
    IFileFingerprint fingerprint,
    ILogger<BookmarkService> log) : IBookmarkService
{
    public async Task<IReadOnlyList<Bookmark>> ListAsync(string documentPath, CancellationToken ct)
    {
        var fp = await fingerprint.ComputeAsync(documentPath, ct).ConfigureAwait(false);
        return await store.ListAsync(fp, ct).ConfigureAwait(false);
    }

    public async Task<Bookmark> AddAsync(string documentPath, int pageIndex, string label, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(label);
        var fp = await fingerprint.ComputeAsync(documentPath, ct).ConfigureAwait(false);
        var bm = Bookmark.Create(pageIndex, label, DateTimeOffset.UtcNow);
        await store.AddAsync(fp, bm, ct).ConfigureAwait(false);
        log.LogDebug("Bookmark added: {Path} page={Page}", documentPath, pageIndex);
        return bm;
    }

    public async Task<bool> RemoveAsync(string documentPath, Guid bookmarkId, CancellationToken ct)
    {
        var fp = await fingerprint.ComputeAsync(documentPath, ct).ConfigureAwait(false);
        return await store.RemoveAsync(fp, bookmarkId, ct).ConfigureAwait(false);
    }

    public async Task<Bookmark?> ToggleAsync(string documentPath, int pageIndex, string label, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(label);
        var fp = await fingerprint.ComputeAsync(documentPath, ct).ConfigureAwait(false);
        var existing = await store.ListAsync(fp, ct).ConfigureAwait(false);
        var onPage = existing.FirstOrDefault(b => b.PageIndex == pageIndex);
        if (onPage is not null)
        {
            await store.RemoveAsync(fp, onPage.Id, ct).ConfigureAwait(false);
            log.LogDebug("Bookmark toggled OFF: {Path} page={Page}", documentPath, pageIndex);
            return null;
        }

        var bm = Bookmark.Create(pageIndex, label, DateTimeOffset.UtcNow);
        await store.AddAsync(fp, bm, ct).ConfigureAwait(false);
        log.LogDebug("Bookmark toggled ON: {Path} page={Page}", documentPath, pageIndex);
        return bm;
    }
}
