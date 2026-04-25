namespace Foliant.Domain;

public sealed record DocumentMetadata(
    string? Title,
    string? Author,
    string? Subject,
    DateTimeOffset? Created,
    DateTimeOffset? Modified,
    IReadOnlyDictionary<string, string> Custom)
{
    public static DocumentMetadata Empty { get; } = new(
        Title: null,
        Author: null,
        Subject: null,
        Created: null,
        Modified: null,
        Custom: new Dictionary<string, string>());
}
