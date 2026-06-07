using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Foliant.Rendering.Html;

/// <summary>
/// A single positioned paint instruction produced by layout, in <em>content (pre-pagination)</em>
/// coordinates: <see cref="Y"/> is measured from the top of the whole chapter, not the page slice.
/// The painter translates by the slice top before drawing. This is a closed hierarchy — the only
/// concrete kinds are <see cref="TextDrawCommand"/> and <see cref="ImageDrawCommand"/>.
/// </summary>
public abstract class DrawCommand
{
    private protected DrawCommand(float x, float y)
    {
        X = x;
        Y = y;
    }

    /// <summary>Left edge of the command in content pixels.</summary>
    public float X { get; }

    /// <summary>Top (for images) or text baseline-origin top (for text) in content pixels.</summary>
    public float Y { get; }

    /// <summary>Vertical extent of the command in content pixels (used for page-slice intersection).</summary>
    public abstract float Height { get; }
}

/// <summary>
/// Draws a single already-wrapped text fragment with a resolved font and colour. The fragment never
/// contains a hard line break; the layout engine emits one command per line fragment.
/// </summary>
public sealed class TextDrawCommand : DrawCommand
{
    /// <summary>Creates a text draw command.</summary>
    /// <param name="text">The fragment text (no line breaks).</param>
    /// <param name="x">Left edge in content pixels.</param>
    /// <param name="y">Top of the line box in content pixels (the painter uses this as the text origin).</param>
    /// <param name="font">Resolved font (family/style/size).</param>
    /// <param name="color">Fill colour.</param>
    /// <param name="lineHeight">Height of the line box in content pixels.</param>
    public TextDrawCommand(string text, float x, float y, Font font, Color color, float lineHeight)
        : base(x, y)
    {
        Text = text;
        Font = font;
        Color = color;
        LineHeight = lineHeight;
    }

    /// <summary>The text to draw.</summary>
    public string Text { get; }

    /// <summary>The resolved font.</summary>
    public Font Font { get; }

    /// <summary>The fill colour.</summary>
    public Color Color { get; }

    /// <summary>Height of this line box in content pixels.</summary>
    public float LineHeight { get; }

    /// <inheritdoc/>
    public override float Height => LineHeight;
}

/// <summary>
/// Draws a decoded raster image into a destination rectangle. The command <em>owns</em> the decoded
/// <see cref="Image{Bgra32}"/> and disposes it; the owning <see cref="HtmlLayout"/> forwards disposal.
/// </summary>
public sealed class ImageDrawCommand : DrawCommand, IDisposable
{
    private bool _disposed;

    /// <summary>Creates an image draw command.</summary>
    /// <param name="image">The decoded image (ownership transfers to this command).</param>
    /// <param name="x">Left edge in content pixels.</param>
    /// <param name="y">Top edge in content pixels.</param>
    /// <param name="width">Destination width in pixels.</param>
    /// <param name="height">Destination height in pixels.</param>
    public ImageDrawCommand(Image<Bgra32> image, float x, float y, float width, float height)
        : base(x, y)
    {
        Image = image;
        Width = width;
        DestHeight = height;
    }

    /// <summary>The decoded image, scaled to the destination size.</summary>
    public Image<Bgra32> Image { get; }

    /// <summary>Destination width in content pixels.</summary>
    public float Width { get; }

    /// <summary>Destination height in content pixels.</summary>
    public float DestHeight { get; }

    /// <inheritdoc/>
    public override float Height => DestHeight;

    /// <summary>Disposes the owned decoded image.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Image.Dispose();
    }
}
