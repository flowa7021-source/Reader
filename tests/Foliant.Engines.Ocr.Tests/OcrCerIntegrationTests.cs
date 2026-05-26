using System.Runtime.InteropServices;
using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using Xunit;

namespace Foliant.Engines.Ocr.Tests;

/// <summary>
/// Measures OCR character error rate (CER) against golden scans (IMPLEMENTATION_PLAN §5.3:
/// CER ≤ 2 % for Cyrillic, ≤ 1 % for Latin). Gated to a Windows stand where the PaddleOCR
/// models (<c>native/paddleocr/</c>) and native runtime (Paddle + OpenCV) are present, plus a
/// scan asset and its co-located <c>*.gt.txt</c> ground truth. Each case is a no-op when any of
/// those is missing, so the suite stays green on hosts without the models/assets (e.g. Linux CI),
/// mirroring the DjVu plugin's <c>DjvuIntegrationTests</c>.
/// </summary>
[Trait("Category", "Slow")]
public sealed class OcrCerIntegrationTests
{
    private static string ModelsRoot =>
        Path.Combine(AppContext.BaseDirectory, "native", "paddleocr");

    private static string AssetsRoot =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "assets");

    [Theory]
    [InlineData("ocr-scan-ru.png", "rus", 0.02)]
    [InlineData("ocr-scan-en.png", "eng", 0.01)]
    public async Task Recognize_KnownScan_CerWithinThreshold(string scanName, string languages, double maxCer)
    {
        if (!ModelsPresent(languages))
        {
            return; // skipped: PaddleOCR models / native runtime not installed on this host.
        }

        string scanPath = Path.Combine(AssetsRoot, scanName);
        string groundTruthPath = Path.ChangeExtension(scanPath, ".gt.txt");
        if (!File.Exists(scanPath) || !File.Exists(groundTruthPath))
        {
            return; // skipped: golden scan + ground-truth asset not present in tests/assets.
        }

        string reference = Normalize(await File.ReadAllTextAsync(groundTruthPath));

        using var engine = new PaddleOcrEngine(NullLogger<PaddleOcrEngine>.Instance);
        using IPageRender render = LoadRender(scanPath);
        TextLayer layer = await engine.RecognizeAsync(render, 0, new OcrOptions(languages), CancellationToken.None);

        string hypothesis = Normalize(string.Join("\n", layer.Runs.Select(r => r.Text)));

        double cer = CharacterErrorRate.Cer(reference, hypothesis);
        cer.Should().BeLessThanOrEqualTo(maxCer);
    }

    private static bool ModelsPresent(string languages)
    {
        OcrModelKind kind = OcrLanguageMap.Resolve(languages);
        string recName = kind == OcrModelKind.Cyrillic ? "cyrillic" : "latin";
        return Directory.Exists(Path.Combine(ModelsRoot, "det"))
            && Directory.Exists(Path.Combine(ModelsRoot, "cls"))
            && Directory.Exists(Path.Combine(ModelsRoot, "rec", recName));
    }

    private static string Normalize(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static IPageRender LoadRender(string scanPath)
    {
        using Mat bgr = Cv2.ImRead(scanPath, ImreadModes.Color);
        using var bgra = new Mat();
        Cv2.CvtColor(bgr, bgra, ColorConversionCodes.BGR2BGRA);

        int w = bgra.Width;
        int h = bgra.Height;
        int stride = w * 4;
        byte[] pixels = new byte[stride * h];
        for (int y = 0; y < h; y++)
        {
            Marshal.Copy(bgra.Ptr(y), pixels, y * stride, stride);
        }

        return new ScanRender(w, h, stride, pixels);
    }

    private sealed class ScanRender(int widthPx, int heightPx, int stride, byte[] pixels) : IPageRender
    {
        public int WidthPx => widthPx;
        public int HeightPx => heightPx;
        public int Stride => stride;
        public ReadOnlyMemory<byte> Bgra32 => pixels;
        public PageSize PageSize => new(widthPx, heightPx);

        public void Dispose()
        {
        }
    }
}
