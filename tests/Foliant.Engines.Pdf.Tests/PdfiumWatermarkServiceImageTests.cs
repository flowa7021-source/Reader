using FluentAssertions;
using Foliant.Domain;
using Foliant.Engines.Pdf;
using PDFiumCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Slow PDFium round-trip для Q-F13 image-watermark: создаём sample PDF + sample PNG,
/// прогоняем сервис в image-режиме, читаем обратно и проверяем что PDFium принял
/// image-объект (page-object-count вырос) и сохранил структуру.
/// </summary>
[Trait("Category", "Slow")]
public sealed class PdfiumWatermarkServiceImageTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly PdfiumWatermarkService _service;

    public PdfiumWatermarkServiceImageTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-wm-img-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
        _service = new PdfiumWatermarkService();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string MakeSamplePdf(double wPt, double hPt)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        var page = builder.AddPage(width: wPt, height: hPt);
        page.AddText("Sample", 12, new UglyToad.PdfPig.Core.PdfPoint(50, 50), font);
        string path = Path.Combine(_tmpDir, $"in-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, builder.Build());
        return path;
    }

    private string MakeSamplePng(int wPx, int hPx)
    {
        using var img = new Image<Rgba32>(wPx, hPx);
        img.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    row[x] = new Rgba32((byte)(x % 256), (byte)(y % 256), 200, 255);
                }
            }
        });
        string path = Path.Combine(_tmpDir, $"img-{Guid.NewGuid():N}.png");
        img.Save(path);
        return path;
    }

    private static int CountObjectsOnFirstPage(string pdfPath)
    {
        byte[] bytes = File.ReadAllBytes(pdfPath);
        var pin = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            var doc = fpdfview.FPDF_LoadMemDocument64(pin.AddrOfPinnedObject(), (ulong)bytes.LongLength, null);
            try
            {
                var page = fpdfview.FPDF_LoadPage(doc, 0);
                try
                {
                    return fpdf_edit.FPDFPageCountObjects(page);
                }
                finally
                {
                    fpdfview.FPDF_ClosePage(page);
                }
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

    [Fact]
    public async Task ApplyAsync_ImageMode_AddsImageObjectToPage()
    {
        string pdf = MakeSamplePdf(600, 800);
        string png = MakeSamplePng(200, 100);
        string target = Path.Combine(_tmpDir, "out.pdf");

        int objsBefore = CountObjectsOnFirstPage(pdf);

        await _service.ApplyAsync(
            pdf,
            new WatermarkSpec(Text: string.Empty, FontSize: 48, Opacity: 0.5, AngleDegrees: 30,
                R: 0, G: 0, B: 0, Range: null, ImagePath: png),
            target,
            CancellationToken.None);

        int objsAfter = CountObjectsOnFirstPage(target);
        objsAfter.Should().BeGreaterThan(objsBefore);
    }

    [Fact]
    public async Task ApplyAsync_ImageMode_RespectsPageRange()
    {
        // Page-range: only page 2 → image объект только на странице с индексом 1.
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        for (int i = 0; i < 3; i++)
        {
            var p = builder.AddPage(width: 400, height: 600);
            p.AddText($"Page {i + 1}", 12, new UglyToad.PdfPig.Core.PdfPoint(50, 50), font);
        }
        string pdfPath = Path.Combine(_tmpDir, "multi.pdf");
        File.WriteAllBytes(pdfPath, builder.Build());

        string png = MakeSamplePng(80, 80);
        string target = Path.Combine(_tmpDir, "out.pdf");

        await _service.ApplyAsync(
            pdfPath,
            new WatermarkSpec(Text: string.Empty, FontSize: 48, Opacity: 1.0, AngleDegrees: 0,
                R: 0, G: 0, B: 0, Range: PageRange.Parse("2"), ImagePath: png),
            target,
            CancellationToken.None);

        // Inspect: page 0 should be unchanged, page 1 should have an extra object, page 2 unchanged.
        byte[] bytes = File.ReadAllBytes(target);
        var pin = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            var doc = fpdfview.FPDF_LoadMemDocument64(pin.AddrOfPinnedObject(), (ulong)bytes.LongLength, null);
            try
            {
                int[] counts = new int[3];
                for (int i = 0; i < 3; i++)
                {
                    var page = fpdfview.FPDF_LoadPage(doc, i);
                    try { counts[i] = fpdf_edit.FPDFPageCountObjects(page); }
                    finally { fpdfview.FPDF_ClosePage(page); }
                }
                counts[1].Should().BeGreaterThan(counts[0]);
                counts[1].Should().BeGreaterThan(counts[2]);
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

    [Fact]
    public async Task ApplyAsync_ImageMode_BlankTextNotRequired()
    {
        // Sanity: image-mode should NOT throw on blank Text (only text-mode requires it).
        string pdf = MakeSamplePdf(400, 400);
        string png = MakeSamplePng(50, 50);

        Func<Task> act = async () => await _service.ApplyAsync(
            pdf,
            new WatermarkSpec(Text: string.Empty, FontSize: 48, Opacity: 0.3, AngleDegrees: 0,
                R: 0, G: 0, B: 0, Range: null, ImagePath: png),
            Path.Combine(_tmpDir, "out.pdf"),
            CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ApplyAsync_TextMode_StillRequiresNonBlankText()
    {
        string pdf = MakeSamplePdf(400, 400);
        Func<Task> act = async () => await _service.ApplyAsync(
            pdf,
            new WatermarkSpec("  ", 48, 0.5, 0, 0, 0, 0), // no ImagePath → text mode, blank text invalid
            Path.Combine(_tmpDir, "out.pdf"),
            CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
