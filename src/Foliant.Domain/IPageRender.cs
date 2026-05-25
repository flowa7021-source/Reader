namespace Foliant.Domain;

/// <summary>
/// Bitmap страницы в BGRA32 (pre-multiplied), готов к WriteableBitmap zero-copy.
/// Owner — caller; кэш владеет своей копией, UI — арендатором.
/// </summary>
public interface IPageRender : IDisposable
{
    int WidthPx { get; }
    int HeightPx { get; }
    int Stride { get; }
    ReadOnlyMemory<byte> Bgra32 { get; }

    /// <summary>
    /// Размер исходной страницы в её собственных единицах (PDF-точки и т.п.) — источник
    /// истины для маппинга пиксель↔страница, не зависящий от текущего zoom UI (масштаб
    /// рендера = <see cref="WidthPx"/> / <c>PageSize.WidthPt</c>).
    /// </summary>
    PageSize PageSize { get; }
}
