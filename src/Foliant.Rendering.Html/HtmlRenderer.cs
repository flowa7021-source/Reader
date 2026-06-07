using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Foliant.Domain;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Foliant.Rendering.Html;

/// <summary>
/// Pure-managed HTML&#8594;BGRA32 renderer: AngleSharp parse &#8594; hand-rolled computed styles
/// &#8594; block+inline layout with greedy word-wrap (SixLabors.Fonts) &#8594; paint
/// (SixLabors.ImageSharp.Drawing) &#8594; <see cref="Image{Bgra32}"/> &#8594; <c>byte[]</c> BGRA32
/// &#8594; <see cref="RenderColorMap.ApplyTheme"/>. No native dependencies. Stateless per call and
/// thread-safe (given a thread-safe <see cref="FontStore"/>).
/// </summary>
public sealed class HtmlRenderer : IHtmlRenderer
{
    private const float PaintDpi = 72f; // Matches the layout measure DPI so px == px.

    private readonly LayoutEngine _layout;
    private readonly ILogger<HtmlRenderer> _log;

    /// <summary>Creates a renderer over a shared font store.</summary>
    /// <param name="fonts">The bundled-font store (share one instance across calls).</param>
    /// <param name="log">Logger for non-fatal diagnostics (parse/decoder warnings).</param>
    /// <exception cref="ArgumentNullException"><paramref name="fonts"/> or <paramref name="log"/> is null.</exception>
    public HtmlRenderer(FontStore fonts, ILogger<HtmlRenderer> log)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
        _layout = new LayoutEngine(fonts, log);
    }

    /// <inheritdoc/>
    public HtmlLayout Layout(HtmlRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Html);
        return _layout.Run(request.Html, request);
    }

    /// <inheritdoc/>
    public HtmlRenderResult RenderPage(HtmlRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Html);

        HtmlViewport vp = request.Viewport;
        int width = Math.Max(1, vp.ContentWidthPx);
        int height = Math.Max(1, vp.PageHeightPx);
        int stride = width * 4;

        using HtmlLayout layout = _layout.Run(request.Html, request);

        int pageIndex = Math.Max(0, vp.PageIndexInChapter);
        float sliceTop = (float)pageIndex * height;
        float sliceBottom = sliceTop + height;

        byte[] buffer = new byte[height * stride];

        using (var image = new Image<Bgra32>(width, height))
        {
            image.Mutate(ctx =>
            {
                ctx.Fill(Color.White);
                PaintCommands(ctx, layout.Commands, sliceTop, sliceBottom);
            });

            image.CopyPixelDataTo(buffer);
        }

        RenderColorMap.ApplyTheme(buffer, request.Theme);

        return new HtmlRenderResult(width, height, stride, buffer, layout.PageCount);
    }

    private void PaintCommands(IImageProcessingContext ctx, IReadOnlyList<DrawCommand> commands, float sliceTop, float sliceBottom)
    {
        foreach (DrawCommand command in commands)
        {
            float top = command.Y;

            // Each command belongs to exactly the page whose slice contains its top edge — cull by
            // top-edge ownership (not intersection), otherwise a line/image straddling a page seam
            // would paint on both the page above (bottom sliver) and below (top sliver, clipped).
            if (top < sliceTop || top >= sliceBottom)
            {
                continue;
            }

            switch (command)
            {
                case TextDrawCommand text:
                    PaintText(ctx, text, sliceTop);
                    break;

                case ImageDrawCommand image:
                    PaintImage(ctx, image, sliceTop);
                    break;

                default:
                    break;
            }
        }
    }

    private static void PaintText(IImageProcessingContext ctx, TextDrawCommand text, float sliceTop)
    {
        if (string.IsNullOrEmpty(text.Text))
        {
            return;
        }

        var options = new RichTextOptions(text.Font)
        {
            Origin = new Vector2(text.X, text.Y - sliceTop),
            Dpi = PaintDpi,
        };

        ctx.DrawText(options, text.Text, Brushes.Solid(text.Color));
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "DrawImage throws when the source lies fully outside the destination; that simply means the image is not on this slice. Robustness contract: painting must not throw.")]
    private void PaintImage(IImageProcessingContext ctx, ImageDrawCommand image, float sliceTop)
    {
        int x = (int)Math.Round(image.X);
        int y = (int)Math.Round(image.Y - sliceTop);

        try
        {
            ctx.DrawImage(image.Image, new Point(x, y), 1f);
        }
        catch (Exception ex)
        {
            // DrawImage throws if the source lies entirely outside the destination bounds; that just
            // means this image does not appear on this slice.
            _log.LogDebug(ex, "Image not composited on this slice (out of bounds).");
        }
    }
}
