using FluentAssertions;
using Foliant.Engines.Pdf;
using PDFiumCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Slow PDFium round-trip для Q-F11: создаём смешанный merge (PDF + PNG + JPEG), читаем
/// обратно общее число страниц + размеры image-страниц через PDFium и проверяем что
/// каждый источник стал страницей в правильном порядке.
/// </summary>
[Trait("Category", "Slow")]
public sealed class PdfiumMergeServiceTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly PdfiumMergeService _merge;

    public PdfiumMergeServiceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-merge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
        _merge = new PdfiumMergeService();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string MakeSamplePdf(double widthPt, double heightPt, string label)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        var page = builder.AddPage(width: widthPt, height: heightPt);
        page.AddText(label, 12, new UglyToad.PdfPig.Core.PdfPoint(50, 50), font);
        string path = Path.Combine(_tmpDir, $"pdf-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, builder.Build());
        return path;
    }

    private string MakeSampleImage(int wPx, int hPx, string ext)
    {
        using var img = new Image<Rgba32>(wPx, hPx);
        // Solid fill so the file isn't trivially empty — content shape doesn't matter.
        img.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    row[x] = new Rgba32((byte)(x % 256), (byte)(y % 256), 128, 255);
                }
            }
        });
        string path = Path.Combine(_tmpDir, $"img-{Guid.NewGuid():N}.{ext}");
        img.Save(path); // sync — keeps `img` alive until the file lands.
        return path;
    }

    private static (int PageCount, List<(float W, float H)> Sizes) InspectPdf(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        var pin = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            var doc = fpdfview.FPDF_LoadMemDocument64(pin.AddrOfPinnedObject(), (ulong)bytes.LongLength, null);
            try
            {
                int pageCount = fpdfview.FPDF_GetPageCount(doc);
                var sizes = new List<(float, float)>(pageCount);
                for (int i = 0; i < pageCount; i++)
                {
                    var page = fpdfview.FPDF_LoadPage(doc, i);
                    try
                    {
                        sizes.Add((fpdfview.FPDF_GetPageWidthF(page), fpdfview.FPDF_GetPageHeightF(page)));
                    }
                    finally
                    {
                        fpdfview.FPDF_ClosePage(page);
                    }
                }
                return (pageCount, sizes);
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
    public async Task MergeAsync_TwoPdfs_AppendsAllPages()
    {
        string a = MakeSamplePdf(400, 300, "A");
        string b = MakeSamplePdf(500, 400, "B");
        string target = Path.Combine(_tmpDir, "out.pdf");

        await _merge.MergeAsync([a, b], target, CancellationToken.None);

        var (count, sizes) = InspectPdf(target);
        count.Should().Be(2);
        sizes[0].W.Should().BeApproximately(400f, 1f);
        sizes[0].H.Should().BeApproximately(300f, 1f);
        sizes[1].W.Should().BeApproximately(500f, 1f);
        sizes[1].H.Should().BeApproximately(400f, 1f);
    }

    [Fact]
    public async Task MergeAsync_PngThenPdf_KeepsOrderAndPixelSizes()
    {
        string png = MakeSampleImage(640, 480, "png");
        string pdf = MakeSamplePdf(800, 600, "PdfPage");
        string target = Path.Combine(_tmpDir, "out.pdf");

        await _merge.MergeAsync([png, pdf], target, CancellationToken.None);

        var (count, sizes) = InspectPdf(target);
        count.Should().Be(2);
        // PNG: 1 px = 1 pt → 640 × 480.
        sizes[0].W.Should().BeApproximately(640f, 1f);
        sizes[0].H.Should().BeApproximately(480f, 1f);
        sizes[1].W.Should().BeApproximately(800f, 1f);
        sizes[1].H.Should().BeApproximately(600f, 1f);
    }

    [Fact]
    public async Task MergeAsync_MixedSources_AllAppended()
    {
        string png = MakeSampleImage(320, 200, "png");
        string jpg = MakeSampleImage(160, 120, "jpg");
        string pdf = MakeSamplePdf(595, 842, "A4-ish");
        string target = Path.Combine(_tmpDir, "out.pdf");

        await _merge.MergeAsync([png, jpg, pdf], target, CancellationToken.None);

        var (count, sizes) = InspectPdf(target);
        count.Should().Be(3);
        sizes[0].W.Should().BeApproximately(320f, 1f);
        sizes[1].W.Should().BeApproximately(160f, 1f);
        sizes[2].W.Should().BeApproximately(595f, 1f);
    }

    [Fact]
    public async Task MergeAsync_FewerThanTwoSources_Throws()
    {
        Func<Task> act = async () => await _merge.MergeAsync(
            [MakeSamplePdf(400, 300, "X")], Path.Combine(_tmpDir, "out.pdf"), CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task MergeAsync_BlankSourcePath_Throws()
    {
        Func<Task> act = async () => await _merge.MergeAsync(
            ["   ", MakeSamplePdf(400, 300, "X")],
            Path.Combine(_tmpDir, "out.pdf"),
            CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
