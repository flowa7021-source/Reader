using FluentAssertions;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Engines.Pdf.Tests;

[Trait("Category", "Integration")]
public sealed class PdfDocumentTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly PdfDocumentLoader _loader = new(NullLogger<PdfDocumentLoader>.Instance);

    public PdfDocumentTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-pdf-doc-tests-" + Guid.NewGuid().ToString("N"));
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
    public async Task PageCount_IsOne_ForMinimalPdf()
    {
        string path = WriteTempPdf();

        await using var doc = await _loader.LoadAsync(path, default);

        doc.PageCount.Should().Be(1);
    }

    [Fact]
    public async Task Metadata_NotNull_ForMinimalPdf()
    {
        string path = WriteTempPdf();

        await using var doc = await _loader.LoadAsync(path, default);

        doc.Metadata.Should().NotBeNull();
    }

    [Fact]
    public async Task GetPageSize_ReturnsA4()
    {
        string path = WriteTempPdf(widthPt: 595, heightPt: 842);

        await using var doc = await _loader.LoadAsync(path, default);

        var size = doc.GetPageSize(0);
        size.WidthPt.Should().BeApproximately(595.0, 1.0);
        size.HeightPt.Should().BeApproximately(842.0, 1.0);
    }

    [Fact]
    public async Task RenderPageAsync_ReturnsNonEmptyBitmap()
    {
        string path = WriteTempPdf();

        await using var doc = await _loader.LoadAsync(path, default);

        using var render = await doc.RenderPageAsync(0, RenderOptions.Default, default);

        render.WidthPx.Should().BeGreaterThan(0);
        render.HeightPx.Should().BeGreaterThan(0);
        render.Bgra32.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RenderPageAsync_DarkTheme_ProducesInvertedBackground()
    {
        string path = WriteTempPdf();

        await using var doc = await _loader.LoadAsync(path, default);

        var darkOpts = RenderOptions.Default with { Theme = RenderTheme.Dark };
        using var render = await doc.RenderPageAsync(0, darkOpts, default);

        // Original background is white (0xFF, 0xFF, 0xFF) in BGR — after inversion it becomes (0, 0, 0).
        var span = render.Bgra32.Span;
        byte b = span[0];
        byte g = span[1];
        byte r = span[2];

        b.Should().BeLessOrEqualTo(50, "B channel should be dark (inverted white background)");
        g.Should().BeLessOrEqualTo(50, "G channel should be dark (inverted white background)");
        r.Should().BeLessOrEqualTo(50, "R channel should be dark (inverted white background)");
    }

    [Fact]
    public async Task DisposeAsync_CanCallTwice()
    {
        string path = WriteTempPdf();

        var doc = await _loader.LoadAsync(path, default);

        await doc.DisposeAsync();

        var act = async () => await doc.DisposeAsync();
        await act.Should().NotThrowAsync();
    }

    private string WriteTempPdf(int widthPt = 595, int heightPt = 842)
    {
        string path = Path.Combine(_tmpDir, $"test-{Guid.NewGuid():N}.pdf");
        byte[] bytes = MinimalPdfFactory.Create(widthPt, heightPt);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
