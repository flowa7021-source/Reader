using System.Diagnostics;
using FluentAssertions;
using Foliant.Domain;
using Foliant.Engines.Image;
using Xunit;

namespace Foliant.Engines.Image.Tests;

/// <summary>
/// Robustness matrix proving the documented best-effort contract of <see cref="ImageDocument.Open"/>
/// against the malformed / hostile corpus (<see cref="MalformedImageCorpus"/>): opening each payload
/// must <b>complete promptly</b> (well under a generous <see cref="Stopwatch"/> budget) and either
/// return a usable one-page document <b>or</b> throw only a <i>tame, catchable</i> exception (a
/// <c>SixLabors.ImageSharp</c> format exception) — never a <see cref="StackOverflowException"/>, hang,
/// OOM or process crash. When a fixture <i>does</i> decode, the document surface
/// (<see cref="IDocument.GetPageSize"/>, <see cref="IDocument.RenderPageAsync"/>,
/// <see cref="IDocument.GetTextLayerAsync"/>) is additionally probed and must complete in budget without
/// throwing. Mirrors <c>Foliant.Engines.Pdf.Tests.MalformedPdfRobustnessTests</c>.
/// </summary>
public sealed class MalformedImageRobustnessTests : IDisposable
{
    private static readonly TimeSpan CompletionBudget = TimeSpan.FromSeconds(30);

    private readonly string _tmpDir;

    /// <summary>Creates a unique temp directory to hold corpus payloads for this test class instance.</summary>
    public MalformedImageRobustnessTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-image-malformed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
    }

    /// <summary>Best-effort removal of the temp directory and all corpus payloads written into it.</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_tmpDir, recursive: true);
        }
        catch (IOException)
        {
            /* best-effort */
        }
        catch (UnauthorizedAccessException)
        {
            /* best-effort */
        }
    }

    /// <summary>Theory rows of a single corpus fixture name.</summary>
    /// <returns>One row per malformed image fixture.</returns>
    public static IEnumerable<object[]> Fixtures()
    {
        foreach (var (name, _) in MalformedImageCorpus.All())
        {
            yield return [name];
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task Open_OnMalformedImage_CompletesPromptlyAndDegradesGracefully(string fixtureName)
    {
        string path = WriteFixture(fixtureName);

        var sw = Stopwatch.StartNew();
        ImageDocument? doc = null;
        try
        {
            doc = ImageDocument.Open(path);
        }
        catch (Exception ex)
        {
            MalformedImageAllowed.AssertTame(ex, fixtureName);
        }

        sw.Stop();
        sw.Elapsed.Should().BeLessThan(
            CompletionBudget,
            "opening the '{0}' image fixture must terminate promptly (no hang/stack-overflow)",
            fixtureName);

        if (doc is not null)
        {
            await using (doc)
            {
                await ProbeDocumentAsync(doc, fixtureName);
            }
        }
    }

    private static async Task ProbeDocumentAsync(ImageDocument doc, string fixtureName)
    {
        doc.PageCount.Should().Be(1, "a decoded image is always a single-page document");

        var sw = Stopwatch.StartNew();

        Action getSize = () => doc.GetPageSize(0);
        getSize.Should().NotThrow("GetPageSize(0) must not throw on decoded fixture '{0}'", fixtureName);

        Func<Task> render = async () =>
            (await doc.RenderPageAsync(0, RenderOptions.Default, CancellationToken.None)).Dispose();
        await render.Should().NotThrowAsync(
            "RenderPageAsync(0) must not throw on decoded fixture '{0}'", fixtureName);

        Func<Task> text = async () => await doc.GetTextLayerAsync(0, CancellationToken.None);
        await text.Should().NotThrowAsync(
            "GetTextLayerAsync(0) must not throw on decoded fixture '{0}'", fixtureName);

        sw.Stop();
        sw.Elapsed.Should().BeLessThan(
            CompletionBudget, "probing decoded fixture '{0}' must complete promptly", fixtureName);
    }

    private string WriteFixture(string fixtureName)
    {
        byte[] bytes = GetFixtureBytes(fixtureName);
        // Use a .png extension uniformly; the opener relies on content sniffing, not the name.
        string path = Path.Combine(_tmpDir, $"{fixtureName}.png");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] GetFixtureBytes(string fixtureName)
    {
        foreach (var (name, bytes) in MalformedImageCorpus.All())
        {
            if (string.Equals(name, fixtureName, StringComparison.Ordinal))
            {
                return bytes;
            }
        }

        throw new InvalidOperationException($"Unknown corpus fixture '{fixtureName}'.");
    }
}
