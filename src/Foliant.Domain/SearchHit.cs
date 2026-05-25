namespace Foliant.Domain;

public sealed record SearchHit(
    string DocFingerprint,
    string Path,
    int PageIndex,
    string Snippet,
    double Rank);

public sealed record SearchQuery(
    string Text,
    int MaxResults = 100,
    string? RestrictToDocFingerprint = null,
    bool MatchCase = false,
    bool MatchWholeWord = false)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);
}
