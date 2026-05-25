using Foliant.Domain;

namespace Foliant.Plugin.DjVu;

internal sealed class DjvuPageRender(int widthPx, int heightPx, int stride, ReadOnlyMemory<byte> data) : IPageRender
{
    public int WidthPx => widthPx;
    public int HeightPx => heightPx;
    public int Stride => stride;
    public ReadOnlyMemory<byte> Bgra32 => data;

    public void Dispose()
    {
        // No unmanaged resources; byte[] is GC-managed.
    }
}
