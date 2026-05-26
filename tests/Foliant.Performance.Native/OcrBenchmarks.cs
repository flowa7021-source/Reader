using BenchmarkDotNet.Attributes;
using Foliant.Application.Services;
using Foliant.Domain;
using Foliant.Engines.Ocr;
using Foliant.Engines.Pdf;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foliant.Performance.Native;

/// <summary>
/// Deferred-run PaddleOCR benches (Windows-only, needs native models under native/paddleocr).
/// The page to recognise is rendered from the sample PDF named by FOLIANT_PERF_SAMPLE_PDF.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Native")]
public class OcrBenchmarks
{
    private const string SampleEnvVar = "FOLIANT_PERF_SAMPLE_PDF";

    private PaddleOcrEngine _engine = null!;
    private IDocument _document = null!;
    private IPageRender _page = null!;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        var samplePath = Environment.GetEnvironmentVariable(SampleEnvVar)
            ?? throw new InvalidOperationException($"Set {SampleEnvVar} to a sample PDF before running Native OCR benches.");

        _engine = new PaddleOcrEngine(NullLogger<PaddleOcrEngine>.Instance);
        var loader = new PdfDocumentLoader(NullLogger<PdfDocumentLoader>.Instance);
        _document = await loader.LoadAsync(samplePath, CancellationToken.None);
        _page = await _document.RenderPageAsync(0, RenderOptions.Default, CancellationToken.None);
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        _page?.Dispose();
        if (_document is not null)
        {
            await _document.DisposeAsync();
        }
        _engine?.Dispose();
    }

    [Benchmark]
    public async Task<int> OcrPageRus()
    {
        var layer = await _engine.RecognizeAsync(_page, 0, new OcrOptions("rus"), CancellationToken.None);
        return layer.Runs.Count;
    }
}
