using System.Runtime.InteropServices;
using FluentAssertions;
using Foliant.Domain;
using Foliant.Engines.Pdf;
using Microsoft.Extensions.Logging.Abstractions;
using PDFiumCore;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Integration-тесты физического redaction'а через настоящий PDFium runtime (нужен libpdfium).
/// Источники генерируются PdfPig'ом (Standard-14 Helvetica) с известными словами на известных
/// строках; результат проверяется через <see cref="PdfDocument"/>.GetTextLayerAsync — слово в
/// области исчезает из текстового слоя, вне области сохраняется.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PdfiumRedactionServiceTests : IDisposable
{
    private const double PageWidthPt = 595;
    private const double PageHeightPt = 842;

    // Где PdfPig рисует каждую строку (X=72), и насколько строки разнесены по вертикали.
    private const double LeftMarginPt = 72;
    private const double FirstLineBaselinePt = PageHeightPt - 72;
    private const double LineStepPt = 40;
    private const int FontSize = 14;

    private readonly string _tmpDir;
    private readonly PdfiumRedactionService _service = new();
    private readonly PdfDocumentLoader _loader = new(NullLogger<PdfDocumentLoader>.Instance);

    public PdfiumRedactionServiceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-redaction-tests-" + Guid.NewGuid().ToString("N"));
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
    public async Task RedactAsync_RegionOverWord_RemovesItFromTextLayer()
    {
        string src = WriteTextPdf("Secret line one", "Public line two", "Public line three");
        string dst = Path.Combine(_tmpDir, "redacted.pdf");
        // Покрываем первую строку целиком (широкий бокс вокруг её baseline).
        var regions = new[] { Region(0, lineIndex: 0) };

        await _service.RedactAsync(src, dst, regions, default);

        string plain = await PlainTextAsync(dst, page: 0);
        plain.Should().NotContain("Secret", "слово под областью должно физически исчезнуть из текстового слоя");
    }

    [Fact]
    public async Task RedactAsync_TextOutsideRegion_IsPreserved()
    {
        string src = WriteTextPdf("Secret line one", "Public line two", "Public line three");
        string dst = Path.Combine(_tmpDir, "redacted.pdf");
        var regions = new[] { Region(0, lineIndex: 0) };

        await _service.RedactAsync(src, dst, regions, default);

        string plain = await PlainTextAsync(dst, page: 0);
        plain.Should().Contain("Public", "текст вне redaction-области должен сохраняться");
    }

    [Fact]
    public async Task RedactAsync_EmptyRegions_ProducesValidPdfWithAllTextIntact()
    {
        string src = WriteTextPdf("Alpha line one", "Beta line two");
        string dst = Path.Combine(_tmpDir, "noop.pdf");

        await _service.RedactAsync(src, dst, Array.Empty<RedactionRegion>(), default);

        File.Exists(dst).Should().BeTrue();
        string plain = await PlainTextAsync(dst, page: 0);
        plain.Should().Contain("Alpha").And.Contain("Beta");
    }

    [Fact]
    public async Task RedactAsync_InvalidPageIndex_Throws()
    {
        string src = WriteTextPdf("Alpha line one");
        string dst = Path.Combine(_tmpDir, "bad.pdf");
        var regions = new[] { new RedactionRegion(5, new AnnotationRect(0, 0, 100, 100)) };

        var act = () => _service.RedactAsync(src, dst, regions, default);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task RedactAsync_OutputReopensAsValidPdf_WithOriginalPageCount()
    {
        string src = WriteTextPdf("Alpha line one", "Beta line two");
        string dst = Path.Combine(_tmpDir, "valid.pdf");
        var regions = new[] { Region(0, lineIndex: 0) };

        await _service.RedactAsync(src, dst, regions, default);

        File.Exists(dst).Should().BeTrue();
        WithDocument(dst, doc => fpdfview.FPDF_GetPageCount(doc).Should().Be(1));
    }

    [Fact]
    public async Task RedactAsync_BlankSourcePath_Throws()
    {
        var act = () => _service.RedactAsync("  ", Path.Combine(_tmpDir, "out.pdf"),
            Array.Empty<RedactionRegion>(), default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>Широкий бокс, накрывающий строку <paramref name="lineIndex"/> целиком: от левого
    /// поля до края страницы, по вертикали — диапазон высотой ~font size вокруг baseline.</summary>
    private static RedactionRegion Region(int pageIndex, int lineIndex)
    {
        double baseline = FirstLineBaselinePt - lineIndex * LineStepPt;
        var rect = new AnnotationRect(
            X: LeftMarginPt - 4,
            Y: baseline - 4,
            Width: PageWidthPt - LeftMarginPt,
            Height: FontSize + 8);
        return new RedactionRegion(pageIndex, rect);
    }

    private async Task<string> PlainTextAsync(string path, int page)
    {
        await using var doc = await _loader.LoadAsync(path, default);
        TextLayer? layer = await doc.GetTextLayerAsync(page, default);
        return layer!.ToPlainText();
    }

    private string WriteTextPdf(params string[] lines)
    {
        using var builder = new PdfDocumentBuilder();
        PdfDocumentBuilder.AddedFont font = builder.AddStandard14Font(Standard14Font.Helvetica);
        PdfPageBuilder pageBuilder = builder.AddPage(PageWidthPt, PageHeightPt);

        double y = FirstLineBaselinePt;
        foreach (string line in lines)
        {
            pageBuilder.AddText(line, FontSize, new PdfPoint(LeftMarginPt, y), font);
            y -= LineStepPt;
        }

        string path = Path.Combine(_tmpDir, $"src-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, builder.Build());
        return path;
    }

    private static void WithDocument(string path, Action<FpdfDocumentT> body)
    {
        fpdfview.FPDF_InitLibrary();
        byte[] bytes = File.ReadAllBytes(path);
        GCHandle pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var doc = fpdfview.FPDF_LoadMemDocument64(pin.AddrOfPinnedObject(), (ulong)bytes.LongLength, null);
            doc.Should().NotBeNull("redaction output must be openable by PDFium");
            try
            {
                body(doc);
            }
            finally
            {
                fpdfview.FPDF_CloseDocument(doc);
            }
        }
        finally
        {
            pin.Free();
        }
    }
}
