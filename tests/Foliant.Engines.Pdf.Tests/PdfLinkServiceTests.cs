using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Read-only integration tests for <see cref="PdfPigLinkService"/>: hand-build a 2-page fixture whose
/// first page carries link annotations (URI-action, GoTo-action, direct <c>/Dest</c> array, direct
/// <c>/Dest (name)</c>), confirm validity with PdfPig, then assert <c>ListLinksAsync</c> projects each
/// link into the right <see cref="Foliant.Domain.PdfLinkAnnotation"/> (containing page, external URI,
/// internal target page). The raw asset yields an empty list; blank path throws. Pure managed PdfPig —
/// no native runtime — so no Slow trait (mirrors <see cref="PdfNamedDestinationServiceTests"/>).
/// </summary>
public sealed class PdfLinkServiceTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly PdfPigLinkService _service = new(NullLogger<PdfPigLinkService>.Instance);

    public PdfLinkServiceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-links-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tmpDir, recursive: true);
        }
        catch
        {
            /* best-effort */
        }
    }

    [Fact]
    public async Task ListLinks_UriAndGoTo_ReturnsBothWithResolvedTargets()
    {
        string path = await WriteFixtureAsync(LinkAnnotationsPdfFactory.Create());

        var list = await _service.ListLinksAsync(path, default);

        list.Should().HaveCount(2);

        // Discovery order on page 0: URI link first, GoTo link second.
        var uriLink = list[0];
        uriLink.PageIndex.Should().Be(0);
        uriLink.Uri.Should().Be("https://example.com");
        uriLink.TargetPageIndex.Should().BeNull("a URI link has no internal target page");

        var goToLink = list[1];
        goToLink.PageIndex.Should().Be(0);
        goToLink.Uri.Should().BeNull("a GoTo link has no external URI");
        goToLink.TargetPageIndex.Should().Be(1, "the GoTo /D array points at page index 1");
    }

    [Fact]
    public async Task ListLinks_FixtureIsValidForPdfPig()
    {
        // The COS reader opens via PdfPig; prove the hand-built fixture is genuinely well-formed.
        string path = await WriteFixtureAsync(LinkAnnotationsPdfFactory.Create());

        using var doc = PdfPigDocument.Open(path);
        doc.NumberOfPages.Should().Be(2);
    }

    [Fact]
    public async Task ListLinks_DirectDestArray_ResolvesTargetPage()
    {
        // /Dest directly on the annot (no /A action), array form [pageRef /Fit] → target page index.
        string path = await WriteFixtureAsync(
            LinkAnnotationsPdfFactory.Create(
                includeUriLink: false, includeGoToLink: false, includeDirectDestArrayLink: true));

        var list = await _service.ListLinksAsync(path, default);

        list.Should().ContainSingle();
        list[0].PageIndex.Should().Be(0);
        list[0].Uri.Should().BeNull();
        list[0].TargetPageIndex.Should().Be(1);
    }

    [Fact]
    public async Task ListLinks_NamedDest_LeavesTargetNull_DocumentedMvp()
    {
        // Name/string destination is not resolved in the MVP: both targets stay null, but the link is
        // still reported (one /Link annotation == one PdfLinkAnnotation).
        string path = await WriteFixtureAsync(
            LinkAnnotationsPdfFactory.Create(
                includeUriLink: false, includeGoToLink: false, includeNamedDestLink: true));

        var list = await _service.ListLinksAsync(path, default);

        list.Should().ContainSingle();
        list[0].PageIndex.Should().Be(0);
        list[0].Uri.Should().BeNull();
        list[0].TargetPageIndex.Should().BeNull("named-destination resolution is out of scope for the MVP");
    }

    [Fact]
    public async Task ListLinks_AllLinkKinds_PreservesDiscoveryOrder()
    {
        string path = await WriteFixtureAsync(
            LinkAnnotationsPdfFactory.Create(
                includeUriLink: true,
                includeGoToLink: true,
                includeDirectDestArrayLink: true,
                includeNamedDestLink: true));

        var list = await _service.ListLinksAsync(path, default);

        list.Should().HaveCount(4);
        list.Should().OnlyContain(x => x.PageIndex == 0);
        list[0].Uri.Should().Be("https://example.com");
        list[1].TargetPageIndex.Should().Be(1);
        list[2].TargetPageIndex.Should().Be(1);
        list[3].Uri.Should().BeNull();
        list[3].TargetPageIndex.Should().BeNull();
    }

    [Fact]
    public async Task ListLinks_RawAsset_IsEmpty()
    {
        (await _service.ListLinksAsync(Asset, default)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ListLinks_BlankPath_Throws(string blank)
    {
        var act = () => _service.ListLinksAsync(blank, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private async Task<string> WriteFixtureAsync(byte[] pdf)
    {
        string path = Path.Combine(_tmpDir, "links-" + Guid.NewGuid().ToString("N") + ".pdf");
        await File.WriteAllBytesAsync(path, pdf, default);
        return path;
    }

    private static string Asset => Path.Combine(ResolveAssetsDir(), "pdf-text-en-10p.pdf");

    private static string ResolveAssetsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Foliant.sln")))
            {
                return Path.Combine(dir.FullName, "tests", "assets");
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root (Foliant.sln).");
    }
}
