using System.Diagnostics.CodeAnalysis;

namespace Foliant.Rendering.Html;

/// <summary>
/// The painted result of one page slice: a tightly-packed BGRA32 bitmap whose byte layout matches
/// <c>Foliant.Domain.IPageRender.Bgra32</c> (the hosting engine wraps it into an <c>IPageRender</c>).
/// </summary>
/// <param name="WidthPx">Bitmap width in pixels.</param>
/// <param name="HeightPx">Bitmap height in pixels.</param>
/// <param name="Stride">Row stride in bytes (always <c>WidthPx * 4</c>).</param>
/// <param name="Bgra32">The pixel buffer, <c>HeightPx * Stride</c> bytes, BGRA byte order, theme already applied.</param>
/// <param name="PageCountInChapter">Total number of page slices the chapter spans at this viewport.</param>
[SuppressMessage(
    "Performance",
    "CA1819:Properties should not return arrays",
    Justification = "This is a transfer DTO: the raw BGRA32 buffer is the whole point. Callers wrap it zero-copy into an IPageRender (which exposes ReadOnlyMemory<byte>); a defensive copy here would defeat that.")]
public sealed record HtmlRenderResult(
    int WidthPx,
    int HeightPx,
    int Stride,
    byte[] Bgra32,
    int PageCountInChapter);
