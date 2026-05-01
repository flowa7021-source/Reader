using Foliant.Domain;

namespace Foliant.Infrastructure.Caching;

/// <summary>
/// Слой 3 кэша рендера: текстовые слои страниц одного документа. Живёт
/// пока документ открыт. В отличие от <see cref="MemoryPageCache"/>
/// (рендер картинок), здесь — структурированный текст для поиска /
/// copy-paste / OCR-сравнения.
///
/// Eviction: нет. Текстовые слои сравнительно компактны (типичная
/// 1080p-страница A4 — пара десятков КБ JSON-эквивалента). Закрытие
/// документа = <see cref="Clear"/>.
///
/// Concurrency: thread-safe (Lock на словаре).
/// </summary>
public sealed class TextStructureCache
{
    private readonly Dictionary<int, TextLayer> _entries = new();
    private readonly Lock _gate = new();

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public bool TryGet(int pageIndex, out TextLayer layer)
    {
        if (pageIndex < 0)
        {
            layer = TextLayer.Empty(0);
            return false;
        }
        lock (_gate)
        {
            if (_entries.TryGetValue(pageIndex, out var value))
            {
                layer = value;
                return true;
            }
        }
        layer = TextLayer.Empty(pageIndex);
        return false;
    }

    /// <summary>Сохранить текстовый слой страницы. Если уже есть — заменяет.
    /// <see cref="TextLayer.PageIndex"/> должен совпадать с <paramref name="pageIndex"/>,
    /// иначе <see cref="ArgumentException"/>.</summary>
    public void Put(int pageIndex, TextLayer layer)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentNullException.ThrowIfNull(layer);
        if (layer.PageIndex != pageIndex)
        {
            throw new ArgumentException(
                $"TextLayer.PageIndex ({layer.PageIndex}) не совпадает с ключом {pageIndex}.",
                nameof(layer));
        }
        lock (_gate)
        {
            _entries[pageIndex] = layer;
        }
    }

    public bool Remove(int pageIndex)
    {
        lock (_gate)
        {
            return _entries.Remove(pageIndex);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }
}
