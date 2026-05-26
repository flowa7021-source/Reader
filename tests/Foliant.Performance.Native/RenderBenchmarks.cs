using BenchmarkDotNet.Attributes;
using Foliant.Domain;
using Foliant.Engines.Pdf;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foliant.Performance.Native;

/// <summary>
/// Deferred-run PDFium render benches (Windows-only). The sample document path is supplied via
/// the FOLIANT_PERF_SAMPLE_PDF environment variable so no large binary asset is committed.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Native")]
public class RenderBenchmarks
{
    private const string SampleEnvVar = "FOLIANT_PERF_SAMPLE_PDF";

    private PdfDocumentLoader _loader = null!;
    private string _samplePath = null!;
    private IDocument? _warmDocument;

    [GlobalSetup]
    public void Setup()
    {
        _samplePath = Environment.GetEnvironmentVariable(SampleEnvVar)
            ?? throw new InvalidOperationException($"Set {SampleEnvVar} to a sample PDF before running Native render benches.");
        _loader = new PdfDocumentLoader(NullLogger<PdfDocumentLoader>.Instance);
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        if (_warmDocument is not null)
        {
            await _warmDocument.DisposeAsync();
        }
    }

    [Benchmark]
    public async Task<int> OpenPdf500Pages()
    {
        await using var doc = await _loader.LoadAsync(_samplePath, CancellationToken.None);
        return doc.PageCount;
    }

    [Benchmark]
    public async Task<int> RenderPage1080p_Cold()
    {
        await using var doc = await _loader.LoadAsync(_samplePath, CancellationToken.None);
        using var render = await doc.RenderPageAsync(0, RenderOptions.Default, CancellationToken.None);
        return render.WidthPx;
    }

    [Benchmark]
    public async Task<int> RenderPage1080p_Warm()
    {
        _warmDocument ??= await _loader.LoadAsync(_samplePath, CancellationToken.None);
        using var render = await _warmDocument.RenderPageAsync(0, RenderOptions.Default, CancellationToken.None);
        return render.WidthPx;
    }
}
