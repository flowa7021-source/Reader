using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Exports a document (represented by its pre-loaded text layers) to a target file.
/// Implementors are responsible for a specific output format (plain text, EPUB, …).
/// </summary>
public interface IDocumentExportService
{
    /// <summary>Lowercase file extension(s) this service can write, e.g. <c>["txt"]</c>.</summary>
    IReadOnlyList<string> SupportedFormats { get; }

    /// <summary>Returns <c>true</c> if <paramref name="targetFormat"/> (case-insensitive)
    /// is in <see cref="SupportedFormats"/>.</summary>
    bool CanExport(string targetFormat);

    /// <summary>
    /// Writes the document text to <paramref name="targetPath"/>.
    /// </summary>
    /// <param name="document">Source document (used for metadata / page count).</param>
    /// <param name="textLayers">Per-page text content, index = page index. May contain
    /// <see cref="TextLayer.Empty"/> entries for unrecognized pages.</param>
    /// <param name="targetPath">Absolute path of the output file. Created or overwritten.</param>
    /// <param name="targetFormat">Desired format (must satisfy <see cref="CanExport"/>).</param>
    /// <param name="progress">Optional per-page progress (0-based count of written pages).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of pages written.</returns>
    Task<int> ExportAsync(
        IDocument document,
        IReadOnlyList<TextLayer> textLayers,
        string targetPath,
        string targetFormat,
        IProgress<int>? progress,
        CancellationToken ct);
}
