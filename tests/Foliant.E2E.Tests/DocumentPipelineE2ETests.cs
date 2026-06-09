using FluentAssertions;
using Foliant.Domain;
using Xunit;

namespace Foliant.E2E.Tests;

/// <summary>
/// Drives every format through the <b>real</b> open path (<c>OpenDocumentUseCase</c> picking the loader
/// by <c>CanLoad</c>) and then through render + text-layer extraction, end to end: PDF/DjVu render
/// natively (PDFium/DjVuLibre), EPUB/FB2/MOBI through the shared AngleSharp→SixLabors HTML engine, and
/// images through ImageSharp. Asserts coarse, platform-independent properties (page count &gt; 0, a
/// non-blank first page, a valid BGRA32 buffer) — the same robustness contract the app relies on.
/// </summary>
[Trait("Category", "E2E")]
public sealed class DocumentPipelineE2ETests
{
    public static IEnumerable<object[]> InBoxFormats()
    {
        yield return ["pdf"];
        yield return ["epub"];
        yield return ["fb2"];
        yield return ["mobi"];
        yield return ["png"];
    }

    [Theory]
    [MemberData(nameof(InBoxFormats))]
    public async Task OpenRenderTextLayer_EachFormat_ProducesUsableFirstPage(string format)
    {
        await using var host = new FoliantPipelineHost();
        string path = MakeFixture(host, format);

        await using IDocument doc = await host.OpenAsync(path);

        doc.PageCount.Should().BeGreaterThan(0, "every fixture has renderable content");

        using IPageRender render = await doc.RenderPageAsync(0, new RenderOptions(Zoom: 1.0), CancellationToken.None);
        render.WidthPx.Should().BeGreaterThan(0);
        render.HeightPx.Should().BeGreaterThan(0);
        render.Stride.Should().BeGreaterThanOrEqualTo(render.WidthPx * 4);
        render.Bgra32.Length.Should().Be(render.Stride * render.HeightPx);
        NonWhitePixels(render).Should().BeGreaterThan(0, "the first page paints actual content, not a blank canvas");

        // Text-bearing formats expose a (possibly empty) text layer for search/index; an image has
        // no text and legitimately returns null per the IDocument contract — every consumer
        // (indexer, search, find-and-redact, the VM) null-checks it.
        TextLayer? text = await doc.GetTextLayerAsync(0, CancellationToken.None);
        if (format != "png")
        {
            text.Should().NotBeNull("text-bearing formats expose a text layer for the search/index pipeline");
        }
    }

    [Fact]
    public async Task TextPdf_ReportsTenPages_AndExtractsText()
    {
        await using var host = new FoliantPipelineHost();

        await using IDocument doc = await host.OpenAsync(E2EFixtures.TextPdf());

        doc.PageCount.Should().Be(10);

        TextLayer? text = await doc.GetTextLayerAsync(0, CancellationToken.None);
        text.Should().NotBeNull();
        text!.Runs.Should().NotBeEmpty("the committed text PDF has an extractable text layer");
    }

    [Fact]
    public async Task UnknownExtension_IsRejectedByEveryLoader()
    {
        await using var host = new FoliantPipelineHost();
        string bogus = host.ScratchPath("not-a-document.xyz");
        await File.WriteAllTextAsync(bogus, "this is not any supported document format");

        Func<Task> open = () => host.OpenAsync(bogus);

        await open.Should().ThrowAsync<InvalidOperationException>("no registered loader CanLoad an unknown format");
    }

    [Fact]
    public async Task AllInBoxPages_Render_WithoutThrowing()
    {
        await using var host = new FoliantPipelineHost();

        foreach (object[] row in InBoxFormats())
        {
            string format = (string)row[0];
            string path = MakeFixture(host, format);
            await using IDocument doc = await host.OpenAsync(path);

            for (int i = 0; i < doc.PageCount; i++)
            {
                using IPageRender render = await doc.RenderPageAsync(i, new RenderOptions(Zoom: 1.0), CancellationToken.None);
                render.Bgra32.Length.Should().Be(render.Stride * render.HeightPx, "page {0} of the {1} fixture renders a well-formed buffer", i, format);
            }
        }
    }

    private static string MakeFixture(FoliantPipelineHost host, string format)
    {
        string dir = Path.Combine(host.TempRoot, "fixtures");
        return format switch
        {
            "pdf" => E2EFixtures.TextPdf(),
            "epub" => E2EFixtures.Epub(dir),
            "fb2" => E2EFixtures.Fb2(dir),
            "mobi" => E2EFixtures.Mobi(dir),
            "png" => E2EFixtures.Png(dir),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "unknown fixture format"),
        };
    }

    private static int NonWhitePixels(IPageRender render)
    {
        ReadOnlySpan<byte> b = render.Bgra32.Span;
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
}
