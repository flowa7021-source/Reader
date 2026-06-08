using Foliant.Domain;
using VersOne.Epub;
using Xunit.Sdk;

namespace Foliant.Engines.Epub.Tests;

/// <summary>
/// The allowed-exception contract for the EPUB malformed-input matrix: a hostile EPUB container may
/// throw only a <i>tame, catchable</i> exception. Any other exception type — or any sign of a critical
/// failure — fails the test, so the matrix's mere completion is itself the proof that opening never
/// produces a <see cref="StackOverflowException"/>, hang or process crash.
/// </summary>
internal static class MalformedEpubAllowed
{
    /// <summary>Asserts <paramref name="ex"/> is a tame, expected failure for opening a malformed EPUB;
    /// otherwise throws to fail the test. <see cref="OperationCanceledException"/> is rethrown unchanged
    /// (it is cooperative cancellation, never a robustness failure).</summary>
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
            $"EPUB fixture '{fixtureName}' threw a non-tame exception "
            + $"{ex.GetType().FullName}: {ex.Message}");
    }

    private static bool IsTame(Exception ex) => ex switch
    {
        // VersOne.Epub surfaces all of its parsing failures under one base type.
        EpubReaderException => true,

        // BCL container/XML/IO failures from a corrupt or truncated archive.
        InvalidDataException => true,
        System.Xml.XmlException => true,
        FileNotFoundException => true,
        DirectoryNotFoundException => true,
        ArgumentException => true,
        IOException => true,

        _ => false,
    };
}
