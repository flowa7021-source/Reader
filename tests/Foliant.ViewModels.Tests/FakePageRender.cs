using Foliant.Domain;

namespace Foliant.ViewModels.Tests;

/// <summary>Лёгкая тестовая реализация <see cref="IPageRender"/> для VM-тестов рендера.</summary>
internal sealed class FakePageRender : IPageRender
{
    public int WidthPx => 100;
    public int HeightPx => 100;
    public int Stride => WidthPx * 4;
    public ReadOnlyMemory<byte> Bgra32 => new byte[Stride * HeightPx];
    public PageSize PageSize => new(72, 72);
    public bool IsDisposed { get; private set; }

    public void Dispose() => IsDisposed = true;
}
