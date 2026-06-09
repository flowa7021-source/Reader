using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Xunit;

namespace Foliant.E2E.Tests;

/// <summary>
/// End-to-end document export: open the text PDF, collect its per-page text layers, and write both a
/// plain-text and a DOCX file through the real <see cref="IDocumentExportService"/> implementations
/// selected by <c>CanExport</c> — exactly as the app's export flow does.
/// </summary>
[Trait("Category", "E2E")]
public sealed class ExportE2ETests
{
    [Fact]
    public async Task ExportPlainText_WritesNonEmptyFileContainingDocumentText()
    {
        await using var host = new FoliantPipelineHost();
        await using IDocument doc = await host.OpenAsync(E2EFixtures.TextPdf());
        IReadOnlyList<TextLayer> layers = await CollectLayersAsync(doc);

        IDocumentExportService txt = Exporter(host, "txt");
        string target = host.ScratchPath("export.txt");

        int pages = await txt.ExportAsync(doc, layers, target, "txt", progress: null, CancellationToken.None);

        pages.Should().BeGreaterThan(0);
        File.Exists(target).Should().BeTrue();
        string written = await File.ReadAllTextAsync(target);
        written.Trim().Should().NotBeEmpty("the text PDF exports real extracted text");

        string firstWord = FirstWord(layers);
        written.Should().Contain(firstWord, "the exported text should contain words from the document");
    }

    [Fact]
    public async Task ExportDocx_WritesAWellFormedZipPackage()
    {
        await using var host = new FoliantPipelineHost();
        await using IDocument doc = await host.OpenAsync(E2EFixtures.TextPdf());
        IReadOnlyList<TextLayer> layers = await CollectLayersAsync(doc);

        IDocumentExportService docx = Exporter(host, "docx");
        string target = host.ScratchPath("export.docx");

        int pages = await docx.ExportAsync(doc, layers, target, "docx", progress: null, CancellationToken.None);

        pages.Should().BeGreaterThan(0);
        File.Exists(target).Should().BeTrue();

        // A .docx is an OOXML zip: it must start with the PK zip local-file-header magic.
        byte[] head = await File.ReadAllBytesAsync(target);
        head.Length.Should().BeGreaterThan(4);
        head[0].Should().Be(0x50); // 'P'
        head[1].Should().Be(0x4B); // 'K'
    }

    private static IDocumentExportService Exporter(FoliantPipelineHost host, string format) =>
        host.Get<IEnumerable<IDocumentExportService>>().First(e => e.CanExport(format));

    private static async Task<IReadOnlyList<TextLayer>> CollectLayersAsync(IDocument doc)
    {
        var layers = new List<TextLayer>(doc.PageCount);
        for (int i = 0; i < doc.PageCount; i++)
        {
            layers.Add(await doc.GetTextLayerAsync(i, CancellationToken.None) ?? TextLayer.Empty(i));
        }

        return layers;
    }

    private static string FirstWord(IReadOnlyList<TextLayer> layers)
    {
        string text = string.Join(' ', layers.SelectMany(l => l.Runs).Select(r => r.Text));
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(w => w.Length >= 4 && w.All(char.IsLetter))
            ?? throw new InvalidOperationException("No searchable word in the document text.");
    }
}
