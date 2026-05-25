using FluentAssertions;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Integration tests for <see cref="PdfDocument"/>.GetTextLayerAsync — verifies the text layer
/// is split into one <see cref="TextRun"/> per text rectangle (≈ per line) instead of a single
/// page-wide run. Needs the native PDFium runtime, hence <c>[Trait("Category","Slow")]</c>.
///
/// <para>The standard <c>MinimalPdfFactory</c> produces a page with no content stream (no text),
/// so it cannot exercise per-line extraction. Text PDFs are instead generated with PdfPig's
/// managed writer (<see cref="PdfDocumentBuilder"/> + a Standard-14 Helvetica font); the same
/// PdfPig dependency is already used by the engine. PdfPig draws text in PDF user space (points,
/// lower-left origin) — the canonical space asserted below.</para>
/// </summary>
[Trait("Category", "Slow")]
public sealed class PdfTextLayerTests : IDisposable
{
    private const double PageWidthPt = 595;
    private const double PageHeightPt = 842;

    private readonly string _tmpDir;
    private readonly PdfDocumentLoader _loader = new(NullLogger<PdfDocumentLoader>.Instance);

    public PdfTextLayerTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-textlayer-tests-" + Guid.NewGuid().ToString("N"));
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
    public async Task GetTextLayer_TextPdf_ReturnsRunPerLine()
    {
        string path = WriteTextPdf("Alpha line one", "Beta line two", "Gamma line three");

        await using var doc = await _loader.LoadAsync(path, default);
        TextLayer? layer = await doc.GetTextLayerAsync(0, default);

        layer.Should().NotBeNull();
        layer!.PageIndex.Should().Be(0);
        layer.Runs.Count.Should().BeGreaterThan(1, "each text line should yield its own rectangle");
    }

    [Fact]
    public async Task GetTextLayer_TextPdf_RunsHaveNonDegenerateBounds()
    {
        string path = WriteTextPdf("Alpha line one", "Beta line two", "Gamma line three");

        await using var doc = await _loader.LoadAsync(path, default);
        TextLayer? layer = await doc.GetTextLayerAsync(0, default);

        layer!.Runs.Should().NotBeEmpty();
        foreach (TextRun run in layer.Runs)
        {
            run.Text.Should().NotBeNullOrWhiteSpace();
            run.W.Should().BeGreaterThan(0, "a non-empty line must have positive width");
            run.H.Should().BeGreaterThan(0, "a non-empty line must have positive height");

            // Canonical space: X/Y is the lower-left corner in PDF points, Y up.
            run.X.Should().BeGreaterThanOrEqualTo(-1);
            run.Y.Should().BeGreaterThanOrEqualTo(-1);
            (run.X + run.W).Should().BeLessThanOrEqualTo(PageWidthPt + 1);
            (run.Y + run.H).Should().BeLessThanOrEqualTo(PageHeightPt + 1);
        }
    }

    [Fact]
    public async Task GetTextLayer_TextPdf_ConcatenatedTextContainsExpectedWords()
    {
        string path = WriteTextPdf("Alpha line one", "Beta line two", "Gamma line three");

        await using var doc = await _loader.LoadAsync(path, default);
        TextLayer? layer = await doc.GetTextLayerAsync(0, default);

        string plain = layer!.ToPlainText();
        plain.Should().Contain("Alpha");
        plain.Should().Contain("Beta");
        plain.Should().Contain("Gamma");
    }

    [Fact]
    public async Task GetTextLayer_DistinctLines_HaveDistinctVerticalPositions()
    {
        string path = WriteTextPdf("Alpha line one", "Beta line two", "Gamma line three");

        await using var doc = await _loader.LoadAsync(path, default);
        TextLayer? layer = await doc.GetTextLayerAsync(0, default);

        var distinctYs = layer!.Runs.Select(r => Math.Round(r.Y, 0)).Distinct().Count();
        distinctYs.Should().BeGreaterThan(1, "lines drawn at different Y should not collapse into one rect");
    }

    [Fact]
    public async Task GetTextLayer_EmptyPage_ReturnsEmptyLayer()
    {
        // MinimalPdfFactory builds a page with no content stream → no extractable text.
        string path = Path.Combine(_tmpDir, $"empty-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, MinimalPdfFactory.Create((int)PageWidthPt, (int)PageHeightPt));

        await using var doc = await _loader.LoadAsync(path, default);
        TextLayer? layer = await doc.GetTextLayerAsync(0, default);

        layer.Should().NotBeNull();
        layer!.Runs.Should().BeEmpty();
    }

    private string WriteTextPdf(params string[] lines)
    {
        using var builder = new PdfDocumentBuilder();
        PdfDocumentBuilder.AddedFont font = builder.AddStandard14Font(Standard14Font.Helvetica);
        PdfPageBuilder page = builder.AddPage(PageWidthPt, PageHeightPt);

        // Draw each line ~40 pt apart from the top so PDFium resolves them as separate rectangles.
        double y = PageHeightPt - 72;
        foreach (string line in lines)
        {
            page.AddText(line, fontSize: 14, new PdfPoint(72, y), font);
            y -= 40;
        }

        string path = Path.Combine(_tmpDir, $"text-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, builder.Build());
        return path;
    }
}
