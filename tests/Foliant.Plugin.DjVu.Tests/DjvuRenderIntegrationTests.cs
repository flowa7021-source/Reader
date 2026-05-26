using FluentAssertions;
using Foliant.Domain;
using Xunit;

namespace Foliant.Plugin.DjVu.Tests;

/// <summary>
/// End-to-end coverage of the out-of-process DjVu engine against a real <c>sample.djvu</c>
/// (a tiny IW44 image) and the real DjVuLibre CLI. No-op when <c>ddjvu</c>/<c>djvused</c>
/// aren't installed, so the suite stays green on hosts without the GPL binaries.
/// </summary>
[Trait("Category", "Slow")]
public sealed class DjvuRenderIntegrationTests
{
    private static string SamplePath => Path.Combine(AppContext.BaseDirectory, "assets", "sample.djvu");

    private static DjvuToolset? Tools => DjvuToolset.TryResolve(out var t) ? t : null;

    [Fact]
    public async Task OpenAndRender_RealDjvu_ProducesConsistentBitmap()
    {
        if (Tools is not { } tools || !File.Exists(SamplePath))
        {
            return; // skipped: DjVuLibre not installed or asset missing.
        }

        await using var doc = await DjvuDocument.OpenAsync(SamplePath, tools, default);

        doc.PageCount.Should().BeGreaterThan(0);

        IPageRender render = await doc.RenderPageAsync(0, RenderOptions.Default, default);
        render.WidthPx.Should().BeGreaterThan(0);
        render.HeightPx.Should().BeGreaterThan(0);
        render.Stride.Should().BeGreaterThanOrEqualTo(render.WidthPx * 4);
        render.Bgra32.Length.Should().Be(render.Stride * render.HeightPx);
        render.PageSize.WidthPt.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetPageSize_RealDjvu_ReturnsPositiveDimensions()
    {
        if (Tools is not { } tools || !File.Exists(SamplePath))
        {
            return;
        }

        await using var doc = await DjvuDocument.OpenAsync(SamplePath, tools, default);

        PageSize size = doc.GetPageSize(0);
        size.WidthPt.Should().BeGreaterThan(0);
        size.HeightPt.Should().BeGreaterThan(0);
    }
}
