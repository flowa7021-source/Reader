using SixLabors.ImageSharp;
using Xunit.Sdk;

namespace Foliant.Engines.Image.Tests;

/// <summary>
/// The allowed-exception contract for the image malformed-input matrix: a hostile image file may throw
/// only a <i>tame, catchable</i> exception. Any other exception type fails the test, so the matrix's
/// mere completion is itself the proof that decoding never produces a <see cref="StackOverflowException"/>,
/// hang or process crash.
/// </summary>
internal static class MalformedImageAllowed
{
    /// <summary>Asserts <paramref name="ex"/> is a tame, expected failure for opening a malformed image;
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
            $"Image fixture '{fixtureName}' threw a non-tame exception "
            + $"{ex.GetType().FullName}: {ex.Message}");
    }

    private static bool IsTame(Exception ex) => ex switch
    {
        // ImageSharp surfaces UnknownImageFormatException (unrecognised) and InvalidImageContentException
        // (recognised-but-corrupt) under one base type.
        ImageFormatException => true,

        // Framework guards from the opener / file layer.
        FileNotFoundException => true,
        DirectoryNotFoundException => true,
        ArgumentException => true,
        IOException => true,

        _ => false,
    };
}
