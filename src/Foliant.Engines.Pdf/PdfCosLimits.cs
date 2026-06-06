namespace Foliant.Engines.Pdf;

/// <summary>Shared safety limits for cos-tree traversal (page trees, name/number trees).</summary>
internal static class PdfCosLimits
{
    /// <summary>Maximum recursion depth when walking <c>/Kids</c> trees. Legitimate PDF trees are
    /// shallow (well under 10); the cap prevents a malformed cyclic or pathologically deep chain from
    /// exhausting the stack (StackOverflowException is uncatchable). Exceeding it stops that branch and
    /// yields a partial/empty result via the best-effort service layer.</summary>
    public const int MaxTreeDepth = 64;
}
