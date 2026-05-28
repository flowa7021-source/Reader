using FluentAssertions;
using Foliant.Application.Services;
using Xunit;

namespace Foliant.Application.Tests.Services;

public sealed class OcrOptionsTests
{
    [Fact]
    public void Default_Languages_AndZeroMinConfidence_AreBackwardCompatible()
    {
        var opts = new OcrOptions();

        opts.Languages.Should().Be("eng+rus");
        opts.MinConfidence.Should().Be(0.0);
    }

    [Fact]
    public void Explicit_MinConfidence_Roundtrips()
    {
        var opts = new OcrOptions(MinConfidence: 0.6);

        opts.MinConfidence.Should().Be(0.6);
    }

    [Fact]
    public void Default_RenderZoom_IsTwo()
    {
        // 2.0 = 192 DPI — sweet spot для PaddleOCR; явный default защищает от регрессии.
        new OcrOptions().RenderZoom.Should().Be(2.0);
    }

    [Fact]
    public void Explicit_RenderZoom_Roundtrips()
    {
        new OcrOptions(RenderZoom: 1.5).RenderZoom.Should().Be(1.5);
    }
}
