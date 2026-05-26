using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Foliant.Application.Services;
using Foliant.Domain;

namespace Foliant.Infrastructure.Export;

/// <summary>
/// Exports document text to an Office Open XML (.docx) file: one Word paragraph
/// per <see cref="TextRun"/> (source order preserved), with a page break between
/// pages. Atomic write: build at a temp path then rename onto the target.
/// </summary>
public sealed class DocxDocumentExportService : IDocumentExportService
{
    private static readonly IReadOnlyList<string> _formats = ["docx"];

    public IReadOnlyList<string> SupportedFormats => _formats;

    public bool CanExport(string targetFormat)
    {
        ArgumentNullException.ThrowIfNull(targetFormat);
        return string.Equals(targetFormat, "docx", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<int> ExportAsync(
        IDocument document,
        IReadOnlyList<TextLayer> textLayers,
        string targetPath,
        string targetFormat,
        IProgress<int>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(textLayers);
        ArgumentNullException.ThrowIfNull(targetPath);
        ArgumentNullException.ThrowIfNull(targetFormat);

        if (!CanExport(targetFormat))
        {
            throw new NotSupportedException($"Format '{targetFormat}' is not supported by {nameof(DocxDocumentExportService)}.");
        }

        string tmp = targetPath + ".tmp";
        try
        {
            // OpenXml SDK is synchronous; keep the async signature off the UI thread.
            int written = await Task.Run(() => BuildDocument(tmp, textLayers, progress, ct), ct)
                .ConfigureAwait(false);

            // FRAGILE: atomic publish — rename only after the package is fully flushed.
            File.Move(tmp, targetPath, overwrite: true);
            return written;
        }
        catch
        {
            try { File.Delete(tmp); }
            catch (IOException) { /* best-effort cleanup */ }
            catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
            throw;
        }
    }

    private static int BuildDocument(
        string path,
        IReadOnlyList<TextLayer> textLayers,
        IProgress<int>? progress,
        CancellationToken ct)
    {
        // FRAGILE: Create writes the OPC package to disk; Dispose flushes + closes it.
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        var body = new Body();
        main.Document = new Document(body);

        int written = 0;
        for (int i = 0; i < textLayers.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (i > 0)
            {
                AppendPageBreak(body);
            }

            AppendPage(body, textLayers[i]);
            written++;
            progress?.Report(written);
        }

        return written;
    }

    private static void AppendPage(Body body, TextLayer layer)
    {
        foreach (var run in layer.Runs)
        {
            body.AppendChild(BuildParagraph(run.Text));
        }
    }

    private static Paragraph BuildParagraph(string text)
    {
        // Preserve whitespace so leading/trailing spaces survive the round-trip.
        var word = new Text(text) { Space = SpaceProcessingModeValues.Preserve };
        return new Paragraph(new Run(word));
    }

    private static void AppendPageBreak(Body body)
    {
        var br = new Break { Type = BreakValues.Page };
        body.AppendChild(new Paragraph(new Run(br)));
    }
}
