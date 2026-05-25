using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// In-memory layer for already-recognized text layers (L2 ahead of the disk IOcrCache).
/// Implemented by Infrastructure.Caching.TextStructureCache; abstracted here so
/// Application does not take a hard dependency on Infrastructure.
/// </summary>
public interface ITextLayerCache
{
    bool TryGet(int pageIndex, out TextLayer layer);
    void Put(int pageIndex, TextLayer layer);
}
