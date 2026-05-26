using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Foliant.Engines.Ocr;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Engines.Ocr.Tests;

public sealed class PaddleOcrEngineTests
{
    [Fact]
    public void Version_IsPositive()
    {
        using var engine = new PaddleOcrEngine(NullLogger<PaddleOcrEngine>.Instance);
        engine.Version.Should().BePositive();
    }

    [Fact]
    public async Task RecognizeAsync_NullRender_Throws()
    {
        using var engine = new PaddleOcrEngine(NullLogger<PaddleOcrEngine>.Instance);

        Func<Task> act = () => engine.RecognizeAsync(null!, 0, new OcrOptions(), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RecognizeAsync_NullOptions_Throws()
    {
        using var engine = new PaddleOcrEngine(NullLogger<PaddleOcrEngine>.Instance);

        Func<Task> act = () => engine.RecognizeAsync(new FakeRender(), 0, null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // Реальный прогон распознавания требует Windows-нативки (Paddle + OpenCV), скачанных
    // моделей в native/paddleocr/ и golden-набора растров с известным текстом — его в
    // tests/assets/ пока нет. Включить вместе с golden-набором (см. план, S8 acceptance / CER).
    [Trait("Category", "Slow")]
    [Fact(Skip = "Needs Windows native runtime + PaddleOCR models + golden raster assets (not yet in tests/assets).")]
    public async Task RecognizeAsync_KnownScan_ReturnsExpectedText()
    {
        await Task.CompletedTask;
    }

    private sealed class FakeRender : IPageRender
    {
        public int WidthPx => 1;
        public int HeightPx => 1;
        public int Stride => 4;
        public ReadOnlyMemory<byte> Bgra32 => new byte[4];
        public PageSize PageSize => new(1, 1);

        public void Dispose()
        {
        }
    }
}
