using BenchmarkDotNet.Attributes;
using Foliant.Domain;
using Foliant.Engines.Pdf;
using Microsoft.Extensions.Logging.Abstractions;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Foliant.Performance;

/// <summary>
/// Phase 1 acceptance benchmarks for PDF open/render (S1 / S3 in PROJECT_BOARD §6.1).
/// Targets (документированы в плане, не enforce'ятся в BDN — это ориентир для DoD review):
/// <list type="bullet">
/// <item>S1 single-page render @ 100% zoom: ≤ 500 ms на 600×800-pt стандартной странице.</item>
/// <item>S3 open 100-page PDF: ≤ 2 s до первого <c>PageCount</c>.</item>
/// </list>
/// Категория <c>PdfNative</c> — отделена от cross-platform-бенчмарков (LRU/FTS/etc.),
/// потому что требует <c>libpdfium</c>/PDFium native runtime. Использует PdfPig.Writer для
/// генерации тестового PDF в <c>GlobalSetup</c> — никаких asset-файлов в репо.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("PdfNative")]
public class PdfRenderBenchmarks
{
    private const int PageCount = 100;
    private const double PageWidthPt = 612;
    private const double PageHeightPt = 792;

    private string _tmpDir = null!;
    private string _pdfPath = null!;
    private PdfDocumentLoader _loader = null!;
    private IDocument _document = null!;

    [GlobalSetup]
    public void Setup()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-bench-pdf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
        _pdfPath = Path.Combine(_tmpDir, "synthetic-100p.pdf");

        // PdfPig builder: Standard-14 Helvetica + per-page random latin-1 paragraph. 100 страниц
        // → файл достаточно «реальный» (~200 KB), чтобы render/open-замеры были осмысленны.
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (int p = 1; p <= PageCount; p++)
        {
            var page = builder.AddPage(PageWidthPt, PageHeightPt);
            string body = $"Page {p} — synthetic benchmark content. Lorem ipsum dolor sit amet, consectetur adipiscing elit.";
            page.AddText(body, fontSize: 11, position: new PdfPoint(72, PageHeightPt - 72), font);
        }
        File.WriteAllBytes(_pdfPath, builder.Build());

        _loader = new PdfDocumentLoader(NullLogger<PdfDocumentLoader>.Instance);
        _document = _loader.LoadAsync(_pdfPath, CancellationToken.None).GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _document.DisposeAsync().AsTask().GetAwaiter().GetResult();
        try
        {
            Directory.Delete(_tmpDir, recursive: true);
        }
        catch (IOException)
        {
            // best-effort temp cleanup
        }
    }

    /// <summary>S3: открытие 100-страничного PDF — от пути на диск до первого <c>PageCount</c>.</summary>
    [Benchmark]
    public int Open_100_Page_Pdf()
    {
        IDocument doc = _loader.LoadAsync(_pdfPath, CancellationToken.None).GetAwaiter().GetResult();
        try
        {
            return doc.PageCount;
        }
        finally
        {
            doc.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>S1: рендер одной страницы при 100% zoom. Документ переоткрыт в Setup'е, чтобы
    /// замер показывал чистую стоимость render'а, а не open+render.</summary>
    [Benchmark]
    public int Render_Single_Page_At_100_Percent()
    {
        IPageRender render = _document.RenderPageAsync(
            0,
            new RenderOptions(Zoom: 1.0),
            CancellationToken.None).GetAwaiter().GetResult();
        try
        {
            return render.WidthPx * render.HeightPx;
        }
        finally
        {
            render.Dispose();
        }
    }
}
