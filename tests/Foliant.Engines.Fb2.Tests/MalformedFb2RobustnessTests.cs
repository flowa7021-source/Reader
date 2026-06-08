using System.Diagnostics;
using FluentAssertions;
using Foliant.Domain;
using Foliant.Engines.Fb2;
using Foliant.Rendering.Html;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Engines.Fb2.Tests;

/// <summary>
/// Robustness matrix proving the documented best-effort contract of <see cref="Fb2Document.Open"/>
/// against the malformed / hostile corpus (<see cref="MalformedFb2Corpus"/>): opening each payload must
/// <b>complete promptly</b> (well under a generous <see cref="Stopwatch"/> budget) and either return a
/// usable document <b>or</b> throw only a <i>tame, catchable</i> exception — never a
/// <see cref="StackOverflowException"/>, hang, OOM or process crash. When a fixture <i>does</i> open,
/// the eager-paginated document surface is additionally probed and must also degrade gracefully within
/// budget. Mirrors <c>Foliant.Engines.Pdf.Tests.MalformedPdfRobustnessTests</c>.
/// </summary>
public sealed class MalformedFb2RobustnessTests : IDisposable
{
    private static readonly TimeSpan CompletionBudget = TimeSpan.FromSeconds(30);

    private static readonly IHtmlRenderer Renderer =
        new HtmlRenderer(new FontStore(), NullLogger<HtmlRenderer>.Instance);

    private readonly string _tmpDir;

    /// <summary>Creates a unique temp directory to hold corpus payloads for this test class instance.</summary>
    public MalformedFb2RobustnessTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-fb2-malformed-" + Guid.NewGuid().ToString("N"));
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
    /// <returns>One row per malformed FB2 fixture.</returns>
    public static IEnumerable<object[]> Fixtures()
    {
        foreach (var (name, _) in MalformedFb2Corpus.All())
        {
            yield return [name];
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task Open_OnMalformedFb2_CompletesPromptlyAndDegradesGracefully(string fixtureName)
    {
        string path = WriteFixture(fixtureName);

        var sw = Stopwatch.StartNew();
        Fb2Document? doc = null;
        try
        {
            doc = Fb2Document.Open(path, Renderer);
        }
        catch (Exception ex)
        {
            MalformedFb2Allowed.AssertTame(ex, fixtureName);
        }

        sw.Stop();
        sw.Elapsed.Should().BeLessThan(
            CompletionBudget,
            "opening the '{0}' FB2 fixture must terminate promptly (no hang/stack-overflow)",
            fixtureName);

        if (doc is not null)
        {
            await using (doc)
            {
                await ProbeDocumentAsync(doc, fixtureName);
            }
        }
    }

    private static async Task ProbeDocumentAsync(Fb2Document doc, string fixtureName)
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
        string path = Path.Combine(_tmpDir, $"{fixtureName}.fb2");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] GetFixtureBytes(string fixtureName)
    {
        foreach (var (name, bytes) in MalformedFb2Corpus.All())
        {
            if (string.Equals(name, fixtureName, StringComparison.Ordinal))
            {
                return bytes;
            }
        }

        throw new InvalidOperationException($"Unknown corpus fixture '{fixtureName}'.");
    }
}
