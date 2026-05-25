namespace Foliant.Infrastructure.Caching;

/// <summary>
/// Декодированная миниатюра страницы: сырые BGRA32-пиксели плюс геометрия,
/// нужная WPF-слою для постройки <c>BitmapSource</c> (как <c>PageSurface</c>).
/// Кодировщик намеренно не используется — пиксели хранятся «как есть».
/// </summary>
public sealed record Thumbnail(int WidthPx, int HeightPx, int Stride, ReadOnlyMemory<byte> Bgra32);
