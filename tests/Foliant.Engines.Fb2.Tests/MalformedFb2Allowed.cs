using Xunit.Sdk;

namespace Foliant.Engines.Fb2.Tests;

/// <summary>
/// The allowed-exception contract for the FB2 malformed-input matrix: a hostile FB2 file may throw only
/// a <i>tame, catchable</i> exception. Any other exception type fails the test, so the matrix's mere
/// completion is itself the proof that opening never produces a <see cref="StackOverflowException"/>,
/// hang or process crash.
/// </summary>
internal static class MalformedFb2Allowed
{
    /// <summary>Asserts <paramref name="ex"/> is a tame, expected failure for opening a malformed FB2;
    /// otherwise throws to fail the test. <see cref="OperationCanceledException"/> is rethrown unchanged.</summary>
    /// <param name="ex">The exception thrown by the opener.</param>
    /// <param name="fixtureName">The corpus fixture name, for diagnostics.</param>
    public static void AssertTame(Exception ex, string fixtureName)
    {
        if (ex is OperationCanceledException)
        {
            throw ex; // cooperative cancellation — not a robustness failure.
        }

        if (IsTame(ex))
        {
            return;
        }

        throw new XunitException(
            $"FB2 fixture '{fixtureName}' threw a non-tame exception "
            + $"{ex.GetType().FullName}: {ex.Message}");
    }

    private static bool IsTame(Exception ex) => ex switch
    {
        // Fb2Document wraps XmlException → InvalidDataException and throws InvalidDataException for a
        // wrong root / namespace; XmlException is allowed defensively in case a parse failure surfaces raw.
        InvalidDataException => true,
        System.Xml.XmlException => true,
        FileNotFoundException => true,
        DirectoryNotFoundException => true,
        ArgumentException => true,
        IOException => true,

        _ => false,
    };
}
