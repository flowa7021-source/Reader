using FluentAssertions;
using Foliant.Domain;
using Foliant.Engines.Pdf;
using Microsoft.Extensions.Logging.Abstractions;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Bates-numbering integration tests via the real PDFium runtime. Each test generates a small
/// text PDF (PdfPig managed writer — same dependency the engine already uses), applies the
/// stamp, then re-reads the text layer through <see cref="PdfDocumentLoader"/> to assert the
/// expected Bates string appears. Mutating PDFium services are uniformly <c>Slow</c>.
/// </summary>
[Trait("Category", "Slow")]
public sealed class PdfiumBatesNumberingServiceTests : IDisposable
{
    private const double PageWidthPt = 595;
    private const double PageHeightPt = 842;

    private readonly string _tmpDir;
    private readonly PdfiumBatesNumberingService _service = new();
    private readonly PdfDocumentLoader _loader = new(NullLogger<PdfDocumentLoader>.Instance);

    public PdfiumBatesNumberingServiceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-bates-tests-" + Guid.NewGuid().ToString("N"));
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
    public async Task ApplyAsync_PrefixAndPadding_StampsSequentialNumbers()
    {
        string src = WriteTextPdf(pageCount: 3);
        string dst = Path.Combine(_tmpDir, "bates.pdf");
        var spec = Spec(prefix: "ACME-", start: 1, digits: 6);

        await _service.ApplyAsync(src, spec, dst, default);

        (await PageTextAsync(dst, 0)).Should().Contain("ACME-000001");
        (await PageTextAsync(dst, 1)).Should().Contain("ACME-000002");
        (await PageTextAsync(dst, 2)).Should().Contain("ACME-000003");
    }

    [Fact]
    public async Task ApplyAsync_CustomStartNumber_FirstPageUsesStart()
    {
        string src = WriteTextPdf(pageCount: 2);
        string dst = Path.Combine(_tmpDir, "bates.pdf");
        var spec = Spec(prefix: "", start: 100, digits: 6);

        await _service.ApplyAsync(src, spec, dst, default);

        (await PageTextAsync(dst, 0)).Should().Contain("000100");
        (await PageTextAsync(dst, 1)).Should().Contain("000101");
    }

    [Fact]
    public async Task ApplyAsync_PageRange_StampsOnlyRangedPagesButKeepsAbsoluteNumbers()
    {
        string src = WriteTextPdf(pageCount: 4);
        string dst = Path.Combine(_tmpDir, "bates.pdf");
        // Range "3" (1-based) → only the 3rd page (0-based index 2) gets stamped, with its
        // absolute Bates number (start 1 + index 2 = 3), not "1".
        var spec = Spec(prefix: "DOC-", start: 1, digits: 4, range: PageRange.Parse("3"));

        await _service.ApplyAsync(src, spec, dst, default);

        (await PageTextAsync(dst, 0)).Should().NotContain("DOC-");
        (await PageTextAsync(dst, 1)).Should().NotContain("DOC-");
        (await PageTextAsync(dst, 2)).Should().Contain("DOC-0003");
        (await PageTextAsync(dst, 3)).Should().NotContain("DOC-");
    }

    [Fact]
    public async Task ApplyAsync_OutputReopensAsValidPdf_WithSamePageCount()
    {
        string src = WriteTextPdf(pageCount: 5);
        string dst = Path.Combine(_tmpDir, "bates.pdf");

        await _service.ApplyAsync(src, Spec("X-", start: 1, digits: 3), dst, default);

        await using var doc = await _loader.LoadAsync(dst, default);
        doc.PageCount.Should().Be(5);
    }

    [Fact]
    public async Task ApplyAsync_SuffixAppended_AppearsInStamp()
    {
        string src = WriteTextPdf(pageCount: 1);
        string dst = Path.Combine(_tmpDir, "bates.pdf");
        var spec = new BatesNumberingSpec(
            Prefix: "ACME-", Suffix: "-CONF", StartNumber: 7, Digits: 5,
            Position: BatesPosition.BottomRight, FontSize: 9, R: 0, G: 0, B: 0);

        await _service.ApplyAsync(src, spec, dst, default);

        (await PageTextAsync(dst, 0)).Should().Contain("ACME-00007-CONF");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ApplyAsync_BlankSourcePath_Throws(string badPath)
    {
        var act = () => _service.ApplyAsync(badPath, Spec("A", 1, 6), Path.Combine(_tmpDir, "o.pdf"), default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ApplyAsync_BlankTargetPath_Throws()
    {
        string src = WriteTextPdf(pageCount: 1);

        var act = () => _service.ApplyAsync(src, Spec("A", 1, 6), "   ", default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ApplyAsync_NonPositiveFontSize_Throws()
    {
        string src = WriteTextPdf(pageCount: 1);
        var spec = new BatesNumberingSpec("A", "", 1, 6, BatesPosition.BottomRight, FontSize: 0, R: 0, G: 0, B: 0);

        var act = () => _service.ApplyAsync(src, spec, Path.Combine(_tmpDir, "o.pdf"), default);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ApplyAsync_ZeroDigits_Throws()
    {
        string src = WriteTextPdf(pageCount: 1);
        var spec = new BatesNumberingSpec("A", "", 1, Digits: 0, BatesPosition.BottomRight, FontSize: 9, R: 0, G: 0, B: 0);

        var act = () => _service.ApplyAsync(src, spec, Path.Combine(_tmpDir, "o.pdf"), default);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0, "ACME-000001")]
    [InlineData(1, "ACME-000002")]
    [InlineData(9, "ACME-000010")]
    public void FormatFor_BuildsZeroPaddedSequentialText(int pageIndex, string expected)
    {
        Spec("ACME-", start: 1, digits: 6).FormatFor(pageIndex).Should().Be(expected);
    }

    private static BatesNumberingSpec Spec(string prefix, int start, int digits, PageRange? range = null) =>
        new(prefix, Suffix: "", StartNumber: start, Digits: digits,
            Position: BatesPosition.BottomRight, FontSize: 9, R: 0, G: 0, B: 0, Range: range);

    private async Task<string> PageTextAsync(string path, int pageIndex)
    {
        await using var doc = await _loader.LoadAsync(path, default);
        TextLayer? layer = await doc.GetTextLayerAsync(pageIndex, default);
        return layer?.ToPlainText() ?? string.Empty;
    }

    private string WriteTextPdf(int pageCount)
    {
        using var builder = new PdfDocumentBuilder();
        PdfDocumentBuilder.AddedFont font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (int i = 0; i < pageCount; i++)
        {
            PdfPageBuilder page = builder.AddPage(PageWidthPt, PageHeightPt);
            page.AddText($"Body line {i + 1}", fontSize: 14, new PdfPoint(72, PageHeightPt - 72), font);
        }

        string path = Path.Combine(_tmpDir, $"src-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, builder.Build());
        return path;
    }
}
