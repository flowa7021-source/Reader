using Foliant.Domain;

namespace Foliant.Infrastructure.Tests.Caching;

/// <summary>Тестовая реализация IPageRender, держит buffer для проверки size accounting.</summary>
internal sealed class FakePageRender(int width, int height) : IPageRender
{
    private bool _disposed;

    public int WidthPx => width;
    public int HeightPx => height;
    public int Stride => width * 4;
    public ReadOnlyMemory<byte> Bgra32 => new byte[Stride * HeightPx];
    public bool IsDisposed => _disposed;

    public void Dispose() => _disposed = true;
}
