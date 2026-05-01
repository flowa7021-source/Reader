using System.Text;
using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Exports document text as UTF-8 plain text: one section per page,
/// pages separated by a line of dashes and a page-number header.
/// Atomic write: temp file + rename ensures no partial output on failure.
/// </summary>
public sealed class PlainTextDocumentExportService : IDocumentExportService
{
    private static readonly IReadOnlyList<string> _formats = ["txt"];

    public IReadOnlyList<string> SupportedFormats => _formats;

    public bool CanExport(string targetFormat)
    {
        ArgumentNullException.ThrowIfNull(targetFormat);
        return string.Equals(targetFormat, "txt", StringComparison.OrdinalIgnoreCase);
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
            throw new NotSupportedException($"Format '{targetFormat}' is not supported by {nameof(PlainTextDocumentExportService)}.");
        }

        string tmp = targetPath + ".tmp";
        try
        {
            var sb = new StringBuilder();
            int written = 0;

            for (int i = 0; i < textLayers.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                sb.AppendLine($"=== Page {i + 1} ===");
                sb.AppendLine(textLayers[i].ToPlainText());
                sb.AppendLine();
                written++;
                progress?.Report(written);
            }

            await File.WriteAllTextAsync(tmp, sb.ToString(), Encoding.UTF8, ct)
                .ConfigureAwait(false);

            File.Move(tmp, targetPath, overwrite: true);
            return written;
        }
        catch
        {
            try { File.Delete(tmp); } catch { /* best-effort cleanup */ }
            throw;
        }
    }
}
