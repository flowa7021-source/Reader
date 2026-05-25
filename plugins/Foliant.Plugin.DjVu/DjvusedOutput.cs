using System.Globalization;
using System.Text;
using Foliant.Domain;

namespace Foliant.Plugin.DjVu;

/// <summary>
/// Pure parsers for <c>djvused</c> textual output: page count, page geometry and the
/// hidden-text S-expression emitted by <c>print-txt</c>. No IO; unit-testable.
/// </summary>
internal static class DjvusedOutput
{
    /// <summary>Parses the integer page count printed by <c>djvused -e "n"</c>.</summary>
    public static int ParsePageCount(string output)
    {
        foreach (string line in EnumerateLines(output))
        {
            if (int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
            {
                return count;
            }
        }

        throw new FormatException($"djvused: could not parse page count from '{output.Trim()}'.");
    }

    /// <summary>
    /// Parses <c>width=W height=H ...</c> from <c>djvused -e "select N; size"</c>.
    /// Returns a zero <see cref="PageSize"/> when the line is absent.
    /// </summary>
    public static PageSize ParsePageSize(string output)
    {
        double width = 0;
        double height = 0;
        foreach (string token in output.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            TryReadKeyValue(token, "width=", ref width);
            TryReadKeyValue(token, "height=", ref height);
        }

        return new PageSize(width, height);
    }

    /// <summary>
    /// Parses the <c>print-txt</c> S-expression into runs. Each <c>(word x0 y0 x1 y1 "text")</c>
    /// becomes a <see cref="TextRun"/> in DjVu image coordinates (origin bottom-left, Y up).
    /// </summary>
    public static TextLayer ParseTextLayer(int pageIndex, string output)
    {
        var runs = new List<TextRun>();
        foreach (string line in EnumerateLines(output))
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("(word", StringComparison.Ordinal) && ParseWord(trimmed) is { } run)
            {
                runs.Add(run);
            }
        }

        return runs.Count == 0 ? TextLayer.Empty(pageIndex) : new TextLayer(pageIndex, runs);
    }

    private static TextRun? ParseWord(string line)
    {
        int quote = line.IndexOf('"', StringComparison.Ordinal);
        if (quote < 0)
        {
            return null;
        }

        string[] nums = line[..quote]
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

        // Tokens: "(word", x0, y0, x1, y1 — need four integers after the tag.
        if (nums.Length < 5 ||
            !TryInt(nums[1], out int x0) || !TryInt(nums[2], out int y0) ||
            !TryInt(nums[3], out int x1) || !TryInt(nums[4], out int y1))
        {
            return null;
        }

        string text = ExtractQuoted(line, quote);
        return string.IsNullOrEmpty(text)
            ? null
            : new TextRun(text, X: x0, Y: y0, W: x1 - x0, H: y1 - y0);
    }

    private static string ExtractQuoted(string line, int openQuote)
    {
        var sb = new StringBuilder();
        for (int i = openQuote + 1; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\\' && i + 1 < line.Length)
            {
                sb.Append(line[++i]);
            }
            else if (c == '"')
            {
                break;
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static void TryReadKeyValue(string token, string key, ref double target)
    {
        if (token.StartsWith(key, StringComparison.Ordinal) &&
            double.TryParse(token.AsSpan(key.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out double v))
        {
            target = v;
        }
    }

    private static bool TryInt(string s, out int value) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static IEnumerable<string> EnumerateLines(string text)
    {
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }
}
