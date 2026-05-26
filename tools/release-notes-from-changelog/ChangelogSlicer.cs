using System.Runtime.CompilerServices;
using System.Text;

[assembly: InternalsVisibleTo("Foliant.Tools.ReleaseNotesFromChangelog.Tests")]

namespace Foliant.Tools.ReleaseNotesFromChangelog;

/// <summary>
/// Extracts a single version's section from a Keep-a-Changelog formatted document.
/// </summary>
internal static class ChangelogSlicer
{
    /// <summary>
    /// Returns the body of the section whose heading matches <paramref name="version"/>,
    /// i.e. the lines between a <c>## [version] ...</c> heading and the next <c>## </c>
    /// heading (exclusive), with surrounding blank lines trimmed. The heading itself is
    /// not included. Comparison ignores a leading <c>v</c> on either side.
    /// </summary>
    /// <param name="changelog">The full CHANGELOG.md contents.</param>
    /// <param name="version">The version to extract, e.g. <c>0.1.0</c> or <c>v0.1.0</c>.</param>
    /// <returns>The trimmed section body.</returns>
    /// <exception cref="ArgumentException">When <paramref name="version"/> is null/whitespace.</exception>
    /// <exception cref="KeyNotFoundException">When no matching section heading exists.</exception>
    public static string Slice(string changelog, string version)
    {
        ArgumentNullException.ThrowIfNull(changelog);
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("Version must be a non-empty string.", nameof(version));
        }

        var wanted = Normalize(version);
        // Split on any newline style; keep it allocation-light and culture-agnostic.
        var lines = changelog.Replace("\r\n", "\n", StringComparison.Ordinal)
                             .Replace('\r', '\n')
                             .Split('\n');

        var start = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (TryGetSectionVersion(lines[i], out var heading) &&
                string.Equals(Normalize(heading), wanted, StringComparison.OrdinalIgnoreCase))
            {
                start = i + 1;
                break;
            }
        }

        if (start < 0)
        {
            throw new KeyNotFoundException($"No section for version '{version}' found in the changelog.");
        }

        var end = lines.Length;
        for (var i = start; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("## ", StringComparison.Ordinal))
            {
                end = i;
                break;
            }
        }

        var sb = new StringBuilder();
        for (var i = start; i < end; i++)
        {
            sb.Append(lines[i]).Append('\n');
        }

        return sb.ToString().Trim('\n', ' ', '\t');
    }

    /// <summary>
    /// Parses the version token out of a Keep-a-Changelog level-2 heading such as
    /// <c>## [0.1.0] - 2026-05-26</c> or <c>## [Unreleased]</c>.
    /// </summary>
    private static bool TryGetSectionVersion(string line, out string version)
    {
        version = string.Empty;
        if (!line.StartsWith("## ", StringComparison.Ordinal))
        {
            return false;
        }

        var open = line.IndexOf('[', StringComparison.Ordinal);
        if (open < 0)
        {
            return false;
        }

        var close = line.IndexOf(']', open + 1);
        if (close < 0)
        {
            return false;
        }

        version = line[(open + 1)..close].Trim();
        return version.Length > 0;
    }

    private static string Normalize(string version)
    {
        var v = version.Trim();
        return v.StartsWith('v') || v.StartsWith('V') ? v[1..] : v;
    }
}
