using Foliant.Tools.ReleaseNotesFromChangelog;

// Usage: release-notes-from-changelog <version> [path-to-changelog]
// Slices the matching section out of a Keep-a-Changelog file and writes it to stdout.
if (args.Length is 0 or > 2)
{
    Console.Error.WriteLine("usage: release-notes-from-changelog <version> [path-to-CHANGELOG.md]");
    return 2;
}

var version = args[0];
var path = args.Length == 2 ? args[1] : "CHANGELOG.md";

if (!File.Exists(path))
{
    Console.Error.WriteLine($"error: changelog not found: {path}");
    return 2;
}

try
{
    var changelog = await File.ReadAllTextAsync(path).ConfigureAwait(false);
    var section = ChangelogSlicer.Slice(changelog, version);
    Console.Out.WriteLine(section);
    return 0;
}
catch (KeyNotFoundException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 2;
}
