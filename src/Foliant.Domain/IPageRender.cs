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
}
