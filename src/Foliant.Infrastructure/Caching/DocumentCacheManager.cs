using System.Collections.Concurrent;

namespace Foliant.Infrastructure.Caching;

/// <summary>
/// Управляет per-document кэш-объектами слоёв 2 и 3: хранит по одному
/// <see cref="ThumbnailCache"/> и <see cref="TextStructureCache"/> на
/// fingerprint документа. При закрытии документа вызовите
/// <see cref="EvictForDocument"/> — кэши очищаются и удаляются из словаря.
///
/// Thread-safe: <see cref="ConcurrentDictionary{TKey,TValue}"/> гарантирует
/// атомарное <c>GetOrAdd</c>; отдельные кэши безопасны внутри себя.
/// </summary>
public sealed class DocumentCacheManager : IDisposable
{
    private readonly ConcurrentDictionary<string, ThumbnailCache> _thumbs =
        new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, TextStructureCache> _texts =
        new(StringComparer.Ordinal);

    private bool _disposed;

    /// <summary>Получить (или создать) кэш миниатюр для документа с данным fingerprint.</summary>
    public ThumbnailCache GetThumbnails(string fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _thumbs.GetOrAdd(fingerprint, _ => new ThumbnailCache());
    }

    /// <summary>Получить (или создать) кэш текстовых слоёв для документа с данным fingerprint.</summary>
    public TextStructureCache GetTextLayers(string fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _texts.GetOrAdd(fingerprint, _ => new TextStructureCache());
    }

    /// <summary>
    /// Сбросить оба кэша для документа и удалить их из внутренних словарей.
    /// No-op если документ ещё не закэширован.
    /// </summary>
    public void EvictForDocument(string fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);

        if (_thumbs.TryRemove(fingerprint, out var thumbs))
        {
            thumbs.Clear();
        }

        if (_texts.TryRemove(fingerprint, out var texts))
        {
            texts.Clear();
        }
    }

    /// <summary>Число fingerprint'ов, для которых есть активный кэш миниатюр.</summary>
    public int TrackedDocumentCount => _thumbs.Count;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        foreach (var c in _thumbs.Values)
        {
            c.Clear();
        }
        _thumbs.Clear();

        foreach (var c in _texts.Values)
        {
            c.Clear();
        }
        _texts.Clear();
    }
}
