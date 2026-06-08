using FluentAssertions;
using Foliant.Domain;
using Foliant.Plugin.DjVu;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Foliant.E2E.Tests;

/// <summary>
/// End-to-end DjVu through the plugin path: register the out-of-process <see cref="DjvuDocumentLoader"/>
/// (the same loader <c>PluginCatalog</c> discovers at runtime) into the pipeline and drive open + render
/// of the committed sample against the real DjVuLibre CLI. No-op when <c>ddjvu</c>/<c>djvused</c> are not
/// installed, so the suite stays green on hosts without the GPL binaries.
/// </summary>
[Trait("Category", "E2E")]
public sealed class DjVuPipelineE2ETests
{
    [Fact]
    public async Task OpenAndRender_SampleDjvu_ThroughThePluginLoader()
    {
        if (!DjvuToolset.TryResolve(out _))
        {
            return; // DjVuLibre not installed — skip.
        }

        await using var host = new FoliantPipelineHost(services =>
            services.AddSingleton<IDocumentLoader>(new DjvuDocumentLoader()));

        await using IDocument doc = await host.OpenAsync(E2EFixtures.SampleDjvu());

        doc.Kind.Should().Be(DocumentKind.Djvu);
        doc.PageCount.Should().BeGreaterThan(0);

        using IPageRender render = await doc.RenderPageAsync(0, new RenderOptions(Zoom: 1.0), CancellationToken.None);
        render.WidthPx.Should().BeGreaterThan(0);
        render.HeightPx.Should().BeGreaterThan(0);
        render.Bgra32.Length.Should().Be(render.Stride * render.HeightPx);
    }
}
