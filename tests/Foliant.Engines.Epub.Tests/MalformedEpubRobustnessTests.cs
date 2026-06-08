using System.Diagnostics;
using FluentAssertions;
using Foliant.Domain;
using Foliant.Engines.Epub;
using Foliant.Rendering.Html;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Engines.Epub.Tests;

/// <summary>
/// Robustness matrix proving the documented best-effort contract of <see cref="EpubDocument.Open"/>
/// against the malformed / hostile container corpus (<see cref="MalformedEpubCorpus"/>): opening each
/// payload must <b>complete promptly</b> (well under a generous <see cref="Stopwatch"/> budget) and
/// either return a usable document <b>or</b> throw only a <i>tame, catchable</i> exception — never a
/// <see cref="StackOverflowException"/>, hang, OOM or process crash. When a fixture <i>does</i> open,
/// the eager-paginated document surface (<see cref="IDocument.GetPageSize"/>,
/// <see cref="IDocument.RenderPageAsync"/>, <see cref="IDocument.GetTextLayerAsync"/>) is additionally
/// probed and must also degrade gracefully within budget. Mirrors
/// <c>Foliant.Engines.Pdf.Tests.MalformedPdfRobustnessTests</c>.
/// </summary>
public sealed class MalformedEpubRobustnessTests : IDisposable
{
    // Generous ceiling: a guarded best-effort open of even a hostile container returns in well under a
    // second. The bound only catches a hang.
    private static readonly TimeSpan CompletionBudget = TimeSpan.FromSeconds(30);

    private static readonly IHtmlRenderer Renderer =
        new HtmlRenderer(new FontStore(), NullLogger<HtmlRenderer>.Instance);

    private readonly string _tmpDir;

    /// <summary>Creates a unique temp directory to hold corpus payloads for this test class instance.</summary>
    public MalformedEpubRobustnessTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-epub-malformed-" + Guid.NewGuid().ToString("N"));
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
    /// <returns>One row per malformed EPUB fixture.</returns>
    public static IEnumerable<object[]> Fixtures()
    {
        foreach (var (name, _) in MalformedEpubCorpus.All())
        {
            yield return [name];
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task Open_OnMalformedEpub_CompletesPromptlyAndDegradesGracefully(string fixtureName)
    {
        string path = WriteFixture(fixtureName);

        var sw = Stopwatch.StartNew();
        EpubDocument? doc = null;
        try
        {
            doc = EpubDocument.Open(path, Renderer);
        }
        catch (Exception ex)
        {
            MalformedEpubAllowed.AssertTame(ex, fixtureName);
        }

        sw.Stop();
        sw.Elapsed.Should().BeLessThan(
            CompletionBudget,
            "opening the '{0}' EPUB fixture must terminate promptly (no hang/stack-overflow)",
            fixtureName);

        if (doc is not null)
        {
            await using (doc)
            {
                await ProbeDocumentAsync(doc, fixtureName);
            }
        }
    }

    /// <summary>For a fixture that opened into a document, probe the eager-paginated surface and assert
    /// every call completes in budget without throwing a non-tame exception.</summary>
    private static async Task ProbeDocumentAsync(EpubDocument doc, string fixtureName)
    {
        doc.PageCount.Should().BeGreaterThanOrEqualTo(0);

        if (doc.PageCount <= 0)
        {
            return;
        }

        var sw = Stopwatch.StartNew();

        Action getSize = () => doc.GetPageSize(0);
        getSize.Should().NotThrow("GetPageSize(0) must not throw on opened fixture '{0}'", fixtureName);

        Func<Task> render = async () =>
            (await doc.RenderPageAsync(0, RenderOptions.Default, CancellationToken.None)).Dispose();
        await render.Should().NotThrowAsync(
            "RenderPageAsync(0) must degrade to a (possibly blank) page on opened fixture '{0}'", fixtureName);

        Func<Task> text = async () => await doc.GetTextLayerAsync(0, CancellationToken.None);
        await text.Should().NotThrowAsync(
            "GetTextLayerAsync(0) must not throw on opened fixture '{0}'", fixtureName);

        sw.Stop();
        sw.Elapsed.Should().BeLessThan(
            CompletionBudget, "probing opened fixture '{0}' must complete promptly", fixtureName);
    }

    private string WriteFixture(string fixtureName)
    {
        byte[] bytes = GetFixtureBytes(fixtureName);
        string path = Path.Combine(_tmpDir, $"{fixtureName}.epub");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] GetFixtureBytes(string fixtureName)
    {
        foreach (var (name, bytes) in MalformedEpubCorpus.All())
        {
            if (string.Equals(name, fixtureName, StringComparison.Ordinal))
            {
                return bytes;
            }
        }

        throw new InvalidOperationException($"Unknown corpus fixture '{fixtureName}'.");
    }
}
