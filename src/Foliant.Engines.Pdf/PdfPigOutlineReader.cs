using System.Diagnostics.CodeAnalysis;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;
using PdfPigBookmarks = UglyToad.PdfPig.Outline.Bookmarks;
using PdfPigBookmarkNode = UglyToad.PdfPig.Outline.BookmarkNode;
using PdfPigDocumentBookmarkNode = UglyToad.PdfPig.Outline.DocumentBookmarkNode;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Читает /Outlines PDF через PdfPig. Возвращает flat-список с глубиной — иерархия теряется,
/// но порядок обхода (pre-order) сохраняется, и <see cref="DocumentOutlineEntry.Depth"/>
/// позволяет восстановить дерево при необходимости. URI/external/embedded-file destinations
/// пропускаются, остаются только page-bound (DocumentBookmarkNode).
///
/// PdfPig — managed, без I/O помимо открытия файла; делает recursive walk бесплатно. На случай
/// битого PDF / отсутствия outline — возвращаем пустой список и пишем warning.
/// </summary>
public sealed class PdfPigOutlineReader : IPdfOutlineReader
{
    private readonly ILogger<PdfPigOutlineReader> _log;

    public PdfPigOutlineReader(ILogger<PdfPigOutlineReader> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Outline read is best-effort: any failure (corrupt PDF, missing Outlines) yields empty list, not a throw.")]
    public Task<IReadOnlyList<DocumentOutlineEntry>> ReadAsync(string pdfPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);

        return Task.Run<IReadOnlyList<DocumentOutlineEntry>>(() =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var doc = PdfPigDocument.Open(pdfPath);
                if (!doc.TryGetBookmarks(out PdfPigBookmarks? bookmarks) || bookmarks is null)
                {
                    return [];
                }

                var result = new List<DocumentOutlineEntry>();
                foreach (var root in bookmarks.Roots)
                {
                    Walk(root, depth: 0, result, ct);
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to read PDF outline from '{Path}'; returning empty.", pdfPath);
                return [];
            }
        }, ct);
    }

    private static void Walk(PdfPigBookmarkNode node, int depth, List<DocumentOutlineEntry> sink, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // PdfPig содержит несколько вариантов узла: DocumentBookmarkNode (page-bound),
        // UriBookmarkNode/ExternalBookmarkNode/EmbeddedBookmarkNode (внешние). Для импорта
        // в bookmarks мы берём только page-bound — остальные не вписываются в наш domain.
        if (node is PdfPigDocumentBookmarkNode pageNode)
        {
            // PdfPig'овский PageNumber — 1-based; наш PageIndex — 0-based.
            int pageIndex = pageNode.Destination.PageNumber - 1;
            if (pageIndex >= 0)
            {
                sink.Add(new DocumentOutlineEntry(pageIndex, pageNode.Title ?? string.Empty, depth));
            }
        }

        foreach (var child in node.Children)
        {
            Walk(child, depth + 1, sink, ct);
        }
    }
}
