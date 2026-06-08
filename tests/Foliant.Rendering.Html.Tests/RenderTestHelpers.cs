using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Foliant.Rendering.Html.Tests;

/// <summary>
/// Shared, coarse, threshold-based assertions and fixtures for the renderer tests. Never asserts
/// pixel-exact values — anti-aliasing differs across platforms — only counts ink and channel
/// dominance.
/// </summary>
internal static class RenderTestHelpers
{
    /// <summary>One shared <see cref="FontStore"/> — loading 12 faces per test would be wasteful.</summary>
    public static FontStore Fonts { get; } = new();

    /// <summary>Builds a renderer over the shared font store with a null logger.</summary>
    public static HtmlRenderer NewRenderer() => new(Fonts, NullLogger<HtmlRenderer>.Instance);

    /// <summary>Builds a render request with the given HTML and otherwise default surface.</summary>
    public static HtmlRenderRequest Request(
        string html,
        HtmlViewport? viewport = null,
        RenderTheme theme = RenderTheme.Original,
        IResourceResolver? resources = null)
        => new(html, resources ?? NullResourceResolver.Instance, viewport ?? HtmlViewport.Default, theme);

    /// <summary>Counts pixels that are not pure white (0xFFFFFF), ignoring alpha.</summary>
    public static int CountNonWhite(HtmlRenderResult result)
    {
        ReadOnlySpan<byte> b = result.Bgra32;
        int count = 0;
        for (int i = 0; i + 3 < b.Length; i += 4)
        {
            if (b[i] != 255 || b[i + 1] != 255 || b[i + 2] != 255)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Counts non-white pixels whose red channel clearly dominates green and blue.</summary>
    public static int CountRedDominant(HtmlRenderResult result)
    {
        // BGRA byte order: [0]=B, [1]=G, [2]=R.
        ReadOnlySpan<byte> b = result.Bgra32;
        int count = 0;
        for (int i = 0; i + 3 < b.Length; i += 4)
        {
            byte blue = b[i];
            byte green = b[i + 1];
            byte red = b[i + 2];

            bool isWhite = blue == 255 && green == 255 && red == 255;
            if (isWhite)
            {
                continue;
            }

            if (red > green + 40 && red > blue + 40)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Reads a single pixel as (B, G, R) at the given coordinates.</summary>
    public static (byte B, byte G, byte R) PixelAt(HtmlRenderResult result, int x, int y)
    {
        int index = (y * result.Stride) + (x * 4);
        byte[] b = result.Bgra32;
        return (b[index], b[index + 1], b[index + 2]);
    }

    /// <summary>True when a resolved colour's red channel clearly dominates green and blue.</summary>
    public static bool IsRedDominant(Color color)
    {
        Rgba32 p = color.ToPixel<Rgba32>();
        return p.R > p.G + 40 && p.R > p.B + 40;
    }

    /// <summary>True when a resolved colour's blue channel clearly dominates red and green.</summary>
    public static bool IsBlueDominant(Color color)
    {
        Rgba32 p = color.ToPixel<Rgba32>();
        return p.B > p.R + 40 && p.B > p.G + 40;
    }

    /// <summary>True when a resolved colour's green channel clearly dominates red and blue.</summary>
    public static bool IsGreenDominant(Color color)
    {
        Rgba32 p = color.ToPixel<Rgba32>();
        return p.G > p.R + 40 && p.G > p.B + 40;
    }

    /// <summary>Encodes a solid-colour PNG of the given size — used to drive the image path.</summary>
    public static byte[] SolidPng(int width, int height, Color color)
    {
        using var image = new Image<Bgra32>(width, height);
        image.Mutate(ctx => ctx.Fill(color));
        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return ms.ToArray();
    }
}

/// <summary>An in-memory <see cref="IResourceResolver"/> mapping <c>src</c> strings to encoded image
/// bytes and <c>href</c> strings to CSS text — for exercising both the image and linked-CSS paths.</summary>
internal sealed class StubResourceResolver : IResourceResolver
{
    private readonly Dictionary<string, byte[]> _map = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _css = new(StringComparer.Ordinal);

    /// <summary>Registers encoded image bytes under a <c>src</c> key.</summary>
    public StubResourceResolver Add(string src, byte[] bytes)
    {
        _map[src] = bytes;
        return this;
    }

    /// <summary>Registers CSS text under a <c>&lt;link href&gt;</c> key.</summary>
    public StubResourceResolver AddCss(string href, string css)
    {
        _css[href] = css;
        return this;
    }

    /// <inheritdoc/>
    public bool TryResolveImage(string src, out ReadOnlyMemory<byte> bytes)
    {
        if (_map.TryGetValue(src, out byte[]? value))
        {
            bytes = value;
            return true;
        }

        bytes = ReadOnlyMemory<byte>.Empty;
        return false;
    }

    /// <inheritdoc/>
    public bool TryResolveCss(string href, out string css)
    {
        if (_css.TryGetValue(href, out string? value))
        {
            css = value;
            return true;
        }

        css = string.Empty;
        return false;
    }
}
