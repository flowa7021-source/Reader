using System.Diagnostics;
using FluentAssertions;
using Foliant.Domain;
using Foliant.Rendering.Html;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Engines.Mobi.Tests;

/// <summary>
/// Robustness matrix proving the documented best-effort contract of <c>MobiDocument.Parse</c> against
/// the malformed / hostile corpus (<see cref="MalformedMobiCorpus"/>): parsing each in-memory payload
/// must <b>complete promptly</b> (well under a generous <see cref="Stopwatch"/> budget) and either
/// return a usable document <b>or</b> throw only a <i>tame, catchable</i> exception
/// (<see cref="InvalidDataException"/>) — never a <see cref="StackOverflowException"/>, hang, OOM or
/// process crash. When a fixture <i>does</i> parse, the eager-paginated document surface is additionally
/// probed and must also degrade gracefully within budget. Mirrors
/// <c>Foliant.Engines.Pdf.Tests.MalformedPdfRobustnessTests</c>.
/// </summary>
public sealed class MalformedMobiRobustnessTests
{
    private static readonly TimeSpan CompletionBudget = TimeSpan.FromSeconds(30);

    private static readonly IHtmlRenderer Renderer =
        new HtmlRenderer(new FontStore(), NullLogger<HtmlRenderer>.Instance);

    /// <summary>Theory rows of a single corpus fixture name.</summary>
    /// <returns>One row per malformed MOBI fixture.</returns>
    public static IEnumerable<object[]> Fixtures()
    {
        foreach (var (name, _) in MalformedMobiCorpus.All())
        {
            yield return [name];
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task Parse_OnMalformedMobi_CompletesPromptlyAndDegradesGracefully(string fixtureName)
    {
        byte[] bytes = GetFixtureBytes(fixtureName);

        var sw = Stopwatch.StartNew();
        MobiDocument? doc = null;
        try
        {
            doc = MobiDocument.Parse(bytes, Renderer);
        }
        catch (Exception ex)
        {
            MalformedMobiAllowed.AssertTame(ex, fixtureName);
        }

        sw.Stop();
        sw.Elapsed.Should().BeLessThan(
            CompletionBudget,
            "parsing the '{0}' MOBI fixture must terminate promptly (no hang/stack-overflow)",
            fixtureName);

        if (doc is not null)
        {
            await using (doc)
            {
                await ProbeDocumentAsync(doc, fixtureName);
            }
        }
    }

    private static async Task ProbeDocumentAsync(MobiDocument doc, string fixtureName)
    {
        doc.PageCount.Should().BeGreaterThanOrEqualTo(0);

        if (doc.PageCount <= 0)
        {
            return;
        }

        var sw = Stopwatch.StartNew();

        Action getSize = () => doc.GetPageSize(0);
        getSize.Should().NotThrow("GetPageSize(0) must not throw on parsed fixture '{0}'", fixtureName);

        Func<Task> render = async () =>
            (await doc.RenderPageAsync(0, RenderOptions.Default, CancellationToken.None)).Dispose();
        await render.Should().NotThrowAsync(
            "RenderPageAsync(0) must degrade to a (possibly blank) page on parsed fixture '{0}'", fixtureName);

        Func<Task> text = async () => await doc.GetTextLayerAsync(0, CancellationToken.None);
        await text.Should().NotThrowAsync(
            "GetTextLayerAsync(0) must not throw on parsed fixture '{0}'", fixtureName);

        sw.Stop();
        sw.Elapsed.Should().BeLessThan(
            CompletionBudget, "probing parsed fixture '{0}' must complete promptly", fixtureName);
    }

    private static byte[] GetFixtureBytes(string fixtureName)
    {
        foreach (var (name, bytes) in MalformedMobiCorpus.All())
        {
            if (string.Equals(name, fixtureName, StringComparison.Ordinal))
            {
                return bytes;
            }
        }

        throw new InvalidOperationException($"Unknown corpus fixture '{fixtureName}'.");
    }
}
