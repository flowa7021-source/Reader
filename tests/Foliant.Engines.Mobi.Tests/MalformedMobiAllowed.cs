using Xunit.Sdk;

namespace Foliant.Engines.Mobi.Tests;

/// <summary>
/// The allowed-exception contract for the MOBI malformed-input matrix: a hostile PalmDB/MOBI container
/// may throw only a <i>tame, catchable</i> exception. Any other exception type fails the test, so the
/// matrix's mere completion is itself the proof that parsing never produces a
/// <see cref="StackOverflowException"/>, hang or process crash.
/// </summary>
internal static class MalformedMobiAllowed
{
    /// <summary>Asserts <paramref name="ex"/> is a tame, expected failure for parsing a malformed MOBI;
    /// otherwise throws to fail the test. <see cref="OperationCanceledException"/> is rethrown unchanged.</summary>
    /// <param name="ex">The exception thrown by the parser.</param>
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
            $"MOBI fixture '{fixtureName}' threw a non-tame exception "
            + $"{ex.GetType().FullName}: {ex.Message}");
    }

    private static bool IsTame(Exception ex) => ex switch
    {
        // MobiDocument.Parse funnels every bad-container / unsupported-compression failure here.
        InvalidDataException => true,
        ArgumentException => true,
        IOException => true,

        _ => false,
    };
}
