using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Xunit;

namespace Foliant.E2E.Tests;

/// <summary>
/// Round-trips a structural PDF edit through the real pipeline: open → edit via the production service
/// (cos-level rewrite) → reopen the written file → assert the change persisted and the document is
/// still intact (page count preserved, still renderable). This is the highest-risk interrelation —
/// the managed cos writer must produce a file the PDFium loader reads back faithfully.
/// </summary>
[Trait("Category", "E2E")]
public sealed class PdfEditRoundTripE2ETests
{
    [Fact]
    public async Task MetadataEdit_PersistsThroughReopen_AndKeepsPages()
    {
        await using var host = new FoliantPipelineHost();
        string source = E2EFixtures.TextPdf();
        string target = host.ScratchPath("metadata-edited.pdf");

        var spec = new PdfMetadataSpec(Title: "E2E Round-Trip Title", Author: "E2E Author", Subject: "E2E Subject");
        await host.Get<IPdfMetadataEditService>().EditAsync(source, target, spec, CancellationToken.None);

        File.Exists(target).Should().BeTrue();

        await using IDocument reopened = await host.OpenAsync(target);
        reopened.PageCount.Should().Be(10, "editing /Info must not drop pages");
        reopened.Metadata.Title.Should().Be("E2E Round-Trip Title");
        reopened.Metadata.Author.Should().Be("E2E Author");

        // The reopened, edited document still renders.
        using IPageRender render = await reopened.RenderPageAsync(0, new RenderOptions(Zoom: 1.0), CancellationToken.None);
        render.Bgra32.Length.Should().Be(render.Stride * render.HeightPx);
    }

    [Fact]
    public async Task MetadataEdit_LeavesSourceFileUntouched()
    {
        await using var host = new FoliantPipelineHost();
        string source = E2EFixtures.TextPdf();
        byte[] before = await File.ReadAllBytesAsync(source);
        string target = host.ScratchPath("edited-copy.pdf");

        await host.Get<IPdfMetadataEditService>()
            .EditAsync(source, target, new PdfMetadataSpec(Title: "Changed"), CancellationToken.None);

        byte[] after = await File.ReadAllBytesAsync(source);
        after.Should().Equal(before, "the edit writes a new file and must never mutate the source in place");
    }
}
