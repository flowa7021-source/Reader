using Foliant.Application.Services;
using Foliant.Domain;

namespace Foliant.ViewModels;

/// <summary>
/// Display-model wrapping a <see cref="SearchHit"/>. Pre-computes
/// <see cref="SnippetSegments"/> so the XAML template can render
/// highlighted matches without calling into services.
/// </summary>
public sealed class SearchHitViewModel
{
    public SearchHit Hit { get; }

    /// <summary>One-based page number for display ("Page 3", not index 2).</summary>
    public int PageNumber => Hit.PageIndex + 1;

    /// <summary>File name (without directory) for compact display in cross-doc results.</summary>
    public string FileName => Path.GetFileName(Hit.Path);

    /// <summary>Full document path, exposed for tooltip / copy.</summary>
    public string DocPath => Hit.Path;

    /// <summary>Relevance rank; higher = better. 0..1 range for FTS back-ends.</summary>
    public double Rank => Hit.Rank;

    /// <summary>
    /// Snippet text split into alternating plain / highlighted segments.
    /// Computed once at construction; never null, never empty when
    /// <see cref="SearchHit.Snippet"/> is non-empty.
    /// </summary>
    public IReadOnlyList<SnippetSegment> SnippetSegments { get; }

    public SearchHitViewModel(SearchHit hit, string query, bool matchCase = false)
    {
        ArgumentNullException.ThrowIfNull(hit);
        ArgumentNullException.ThrowIfNull(query);

        Hit = hit;
        SnippetSegments = SnippetHighlighter.Highlight(hit.Snippet, query, matchCase);
    }
}
