using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Xunit;

namespace Foliant.E2E.Tests;

/// <summary>
/// End-to-end annotation flow: add annotations through the real service (JSON sidecar persistence),
/// read them back, and export a PDF with the annotations embedded — reopening the exported PDF to
/// confirm it stays a valid, page-preserving, renderable document.
/// </summary>
[Trait("Category", "E2E")]
public sealed class AnnotationE2ETests
{
    private static readonly DateTimeOffset When = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AddAnnotations_PersistThroughTheSidecarStore()
    {
        await using var host = new FoliantPipelineHost();
        string pdf = E2EFixtures.TextPdf();
        IAnnotationService annotations = host.Get<IAnnotationService>();

        var highlight = Annotation.Highlight(0, new AnnotationRect(10, 10, 100, 20), "#FFFF00", When);
        var note = Annotation.StickyNote(1, new AnnotationRect(20, 20, 30, 30), "E2E note", "#FF8800", When);
        await annotations.AddAsync(pdf, highlight, CancellationToken.None);
        await annotations.AddAsync(pdf, note, CancellationToken.None);

        IReadOnlyList<Annotation> listed = await annotations.ListAsync(pdf, CancellationToken.None);

        listed.Should().HaveCount(2);
        listed.Should().Contain(a => a.Id == highlight.Id && a.Kind == AnnotationKind.Highlight);
        listed.Should().Contain(a => a.Id == note.Id && a.Text == "E2E note");
    }

    [Fact]
    public async Task RemoveAnnotation_TakesEffect()
    {
        await using var host = new FoliantPipelineHost();
        string pdf = E2EFixtures.TextPdf();
        IAnnotationService annotations = host.Get<IAnnotationService>();

        var ann = Annotation.Highlight(0, new AnnotationRect(0, 0, 50, 10), "#00FF00", When);
        await annotations.AddAsync(pdf, ann, CancellationToken.None);

        (await annotations.RemoveAsync(pdf, ann.Id, CancellationToken.None)).Should().BeTrue();
        (await annotations.ListAsync(pdf, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task ExportAnnotatedPdf_ProducesAValidReopenablePdf()
    {
        await using var host = new FoliantPipelineHost();
        string source = E2EFixtures.TextPdf();
        string target = host.ScratchPath("annotated.pdf");

        var annotations = new[]
        {
            Annotation.Highlight(0, new AnnotationRect(50, 50, 120, 16), "#FFFF00", When),
            Annotation.Rectangle(0, new AnnotationRect(40, 40, 140, 40), "#FF0000", When),
        };

        await host.Get<IAnnotatedPdfExportService>().ExportAsync(source, annotations, target, CancellationToken.None);

        File.Exists(target).Should().BeTrue();
        new FileInfo(target).Length.Should().BeGreaterThan(0);

        await using IDocument reopened = await host.OpenAsync(target);
        reopened.PageCount.Should().Be(10, "embedding annotations must not change the page count");
        using IPageRender render = await reopened.RenderPageAsync(0, new RenderOptions(Zoom: 1.0), CancellationToken.None);
        render.Bgra32.Length.Should().Be(render.Stride * render.HeightPx);
    }
}
