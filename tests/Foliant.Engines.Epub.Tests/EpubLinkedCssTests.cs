using FluentAssertions;
using Foliant.Domain;
using Foliant.Rendering.Html;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Engines.Epub.Tests;

/// <summary>
/// End-to-end proof that an EPUB's linked stylesheet flows through the EPUB resolver into the shared
/// HTML renderer's author-CSS cascade: a <c>&lt;link rel="stylesheet"&gt;</c> in the chapter head is
/// resolved out of the archive (VersOne <c>Content.Css</c>) by <see cref="EpubResourceResolver"/>,
/// parsed, and applied over the user-agent defaults. Asserts on rendered pixels (coarse colour
/// dominance) rather than exact values, matching the renderer test style.
/// </summary>
public sealed class EpubLinkedCssTests : IDisposable
{
    private static readonly IHtmlRenderer Renderer = new HtmlRenderer(new FontStore(), NullLogger<HtmlRenderer>.Instance);

    private readonly string _tmpDir;

    public EpubLinkedCssTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-epub-css-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static int CountRedDominant(IPageRender render)
    {
        ReadOnlySpan<byte> b = render.Bgra32.Span;
        int count = 0;
        for (int i = 0; i + 3 < b.Length; i += 4)
        {
            byte blue = b[i];
            byte green = b[i + 1];
            byte red = b[i + 2];
            if (red == 255 && green == 255 && blue == 255)
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

    private async Task<int> RenderRedCountAsync(string path)
    {
        await using IDocument doc = EpubDocument.Open(path, Renderer);
        using IPageRender render = await doc.RenderPageAsync(0, new RenderOptions(Zoom: 1.0), CancellationToken.None);
        return CountRedDominant(render);
    }

    [Fact]
    public async Task RenderPage_LinkedCssClassSelector_AppliesColor()
    {
        string path = EpubTestFactory.CreateWithCss(
            _tmpDir, "Styled", "Author",
            cssHref: "styles/main.css",
            cssBody: ".accent{color:#e00000}",
            "<p class=\"accent\">RED RED RED RED RED RED RED RED</p>");

        (await RenderRedCountAsync(path)).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RenderPage_LinkedCssTagSelector_AppliesColor()
    {
        string path = EpubTestFactory.CreateWithCss(
            _tmpDir, "Styled", "Author",
            cssHref: "main.css",
            cssBody: "p{color:red}",
            "<p>crimson words spread across the page widely</p>");

        (await RenderRedCountAsync(path)).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RenderPage_NoCss_DoesNotProduceRed()
    {
        string path = EpubTestFactory.Create(
            _tmpDir, "Plain", "Author",
            "<p>ordinary black body text on the page</p>");

        (await RenderRedCountAsync(path)).Should().Be(0);
    }

    [Fact]
    public async Task RenderPage_LinkedCssDisplayNone_RemovesPaintedInk()
    {
        // display:none must drop the element from the rendered layout. The grubby text layer still
        // indexes the raw markup (search needs it), so we assert on the painted text via ink instead.
        string visiblePath = EpubTestFactory.CreateWithCss(
            _tmpDir, "Vis", "Author",
            cssHref: "s.css",
            cssBody: ".x{color:black}",
            "<p class=\"x\">aaaaaaaaaa bbbbbbbbbb cccccccccc dddddddddd</p>");
        string hiddenPath = EpubTestFactory.CreateWithCss(
            _tmpDir, "Hid", "Author",
            cssHref: "s.css",
            cssBody: ".x{display:none}",
            "<p class=\"x\">aaaaaaaaaa bbbbbbbbbb cccccccccc dddddddddd</p>");

        await using IDocument visibleDoc = EpubDocument.Open(visiblePath, Renderer);
        await using IDocument hiddenDoc = EpubDocument.Open(hiddenPath, Renderer);
        using IPageRender visible = await visibleDoc.RenderPageAsync(0, new RenderOptions(Zoom: 1.0), CancellationToken.None);
        using IPageRender hidden = await hiddenDoc.RenderPageAsync(0, new RenderOptions(Zoom: 1.0), CancellationToken.None);

        int CountInk(IPageRender r)
        {
            ReadOnlySpan<byte> b = r.Bgra32.Span;
            int n = 0;
            for (int i = 0; i + 3 < b.Length; i += 4)
            {
                if (b[i] != 255 || b[i + 1] != 255 || b[i + 2] != 255)
                {
                    n++;
                }
            }

            return n;
        }

        CountInk(hidden).Should().BeLessThan(CountInk(visible));
    }
}
