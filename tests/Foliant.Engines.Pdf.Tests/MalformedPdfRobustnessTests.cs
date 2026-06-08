using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Broad robustness/security matrix proving the documented best-effort contract of <b>every</b>
/// PdfPig-based read/inspect service: run each service against each malformed / hostile payload in
/// <see cref="MalformedPdfCorpus"/> and assert the call <b>completes promptly</b> (well under a generous
/// budget, via <see cref="Stopwatch"/>) and <b>returns non-null</b> — i.e. it never throws and never
/// hangs. This generalises <c>PdfCosCycleGuardTests</c> (two cycle fixtures × two services) into the full
/// corpus × service cross-product. Security value: a malicious PDF must never crash or hang the reader.
///
/// <para>
/// Native PDFium services (render / OCG) are deliberately excluded: the native runtime is not present on
/// the cross-platform (Linux) test runner, so they are out of scope for this managed-only fuzz matrix.
/// </para>
/// </summary>
public sealed class MalformedPdfRobustnessTests : IDisposable
{
    // Generous ceiling. A guarded best-effort read of even a hostile payload returns in well under a
    // second (parse fails fast; the depth cap bounds cyclic/deep walks). The bound only catches a hang.
    private static readonly TimeSpan CompletionBudget = TimeSpan.FromSeconds(30);

    private readonly string _tmpDir;

    /// <summary>Creates a unique temp directory to hold corpus payloads for this test class instance.</summary>
    public MalformedPdfRobustnessTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-malformed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
    }

    /// <summary>Best-effort removal of the temp directory and all corpus payloads written into it.</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_tmpDir, recursive: true);
        }
        catch
        {
            /* best-effort */
        }
    }

    /// <summary>
    /// The cross-product of every corpus fixture name × every service name. Each xUnit case is therefore
    /// one (fixture, service) cell, so a failing matrix cell names exactly which service broke on which
    /// hostile payload.
    /// </summary>
    /// <returns>Theory rows of <c>[fixtureName, serviceName]</c>.</returns>
    public static IEnumerable<object[]> FixtureServiceMatrix()
    {
        foreach (var (fixtureName, _) in MalformedPdfCorpus.All())
        {
            foreach (string serviceName in ServiceRunners.Keys)
            {
                yield return [fixtureName, serviceName];
            }
        }
    }

    [Theory]
    [MemberData(nameof(FixtureServiceMatrix))]
    public async Task Service_OnMalformedPdf_CompletesPromptlyWithoutThrow(string fixtureName, string serviceName)
    {
        byte[] bytes = GetFixtureBytes(fixtureName);
        string path = await WriteFixtureAsync(fixtureName, bytes);
        Func<string, CancellationToken, Task> run = ServiceRunners[serviceName];

        var sw = Stopwatch.StartNew();
        Func<Task> act = () => run(path, default);

        await act.Should().NotThrowAsync(
            "service '{0}' must degrade to an empty/partial result on the '{1}' fixture, never throw",
            serviceName, fixtureName);
        sw.Stop();

        sw.Elapsed.Should().BeLessThan(
            CompletionBudget,
            "service '{0}' must terminate promptly on the '{1}' fixture (the depth guard bounds cyclic/deep walks)",
            serviceName, fixtureName);
    }

    /// <summary>
    /// Dedicated cell for the high-value composite: the preflight service composes four sub-inspectors
    /// plus a PdfPig structural pass, so it is exercised both inside the matrix above (as one runner) and
    /// here, where we additionally assert its returned report object is non-null for every fixture.
    /// </summary>
    [Theory]
    [MemberData(nameof(FixtureNames))]
    public async Task PreflightService_OnMalformedPdf_ReturnsNonNullReportPromptly(string fixtureName)
    {
        byte[] bytes = GetFixtureBytes(fixtureName);
        string path = await WriteFixtureAsync(fixtureName, bytes);
        var service = NewPreflightService();

        var sw = Stopwatch.StartNew();
        Domain.PdfPreflightReport report = null!;
        Func<Task> act = async () => report = await service.PreflightAsync(path, default);

        await act.Should().NotThrowAsync(
            "preflight must compose its best-effort sub-inspectors into a report on the '{0}' fixture, never throw",
            fixtureName);
        sw.Stop();

        report.Should().NotBeNull("preflight always returns a (possibly all-default) report, never null");
        sw.Elapsed.Should().BeLessThan(
            CompletionBudget, "preflight must terminate promptly on the '{0}' fixture", fixtureName);
    }

    /// <summary>Theory rows of a single corpus fixture name — drives the preflight-specific test.</summary>
    /// <returns>One row per corpus fixture.</returns>
    public static IEnumerable<object[]> FixtureNames()
    {
        foreach (var (fixtureName, _) in MalformedPdfCorpus.All())
        {
            yield return [fixtureName];
        }
    }

    /// <summary>
    /// Name → invocation for every best-effort read/inspect service under test. Each runner constructs the
    /// service with <see cref="NullLogger{T}.Instance"/>, calls its public read method, and (where the
    /// contract promises a non-null result) asserts non-null — so the matrix proves both "no throw/hang"
    /// and "best-effort non-null result". Insertion order is preserved for stable test display names.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Func<string, CancellationToken, Task>> ServiceRunners =
        new Dictionary<string, Func<string, CancellationToken, Task>>(StringComparer.Ordinal)
        {
            ["Font"] = async (path, ct) =>
            {
                var result = await new PdfPigFontService(NullLogger<PdfPigFontService>.Instance)
                    .ListFontsAsync(path, ct);
                result.Should().NotBeNull();
            },
            ["Link"] = async (path, ct) =>
            {
                var result = await new PdfPigLinkService(NullLogger<PdfPigLinkService>.Instance)
                    .ListLinksAsync(path, ct);
                result.Should().NotBeNull();
            },
            ["OutputIntent"] = async (path, ct) =>
            {
                var result = await new PdfPigOutputIntentService(NullLogger<PdfPigOutputIntentService>.Instance)
                    .ListAsync(path, ct);
                result.Should().NotBeNull();
            },
            ["Sanitization"] = async (path, ct) =>
            {
                var result = await new PdfPigSanitizationService(NullLogger<PdfPigSanitizationService>.Instance)
                    .ScanAsync(path, ct);
                result.Should().NotBeNull();
            },
            ["NamedDestination"] = async (path, ct) =>
            {
                var result = await new PdfPigNamedDestinationService(
                        NullLogger<PdfPigNamedDestinationService>.Instance)
                    .ListAsync(path, ct);
                result.Should().NotBeNull();
            },
            ["OutlineInspector"] = async (path, ct) =>
            {
                var result = await new PdfPigOutlineInspector(NullLogger<PdfPigOutlineInspector>.Instance)
                    .ReadRichAsync(path, ct);
                result.Should().NotBeNull();
            },
            ["PageLabel"] = async (path, ct) =>
            {
                var result = await new PdfPigPageLabelService(NullLogger<PdfPigPageLabelService>.Instance)
                    .ReadAsync(path, ct);
                result.Should().NotBeNull();
            },
            ["Attachment"] = async (path, ct) =>
            {
                var result = await new PdfPigAttachmentService(NullLogger<PdfPigAttachmentService>.Instance)
                    .ListAsync(path, ct);
                result.Should().NotBeNull();
            },
            ["ViewerPreferences"] = async (path, ct) =>
            {
                var result = await new PdfPigViewerPreferencesService(
                        NullLogger<PdfPigViewerPreferencesService>.Instance)
                    .ReadAsync(path, ct);
                result.Should().NotBeNull();
            },
            ["CustomProperties"] = async (path, ct) =>
            {
                var result = await new PdfPigCustomPropertiesService(
                        NullLogger<PdfPigCustomPropertiesService>.Instance)
                    .ListAsync(path, ct);
                result.Should().NotBeNull();
            },
            // XMP is the one read that legitimately returns null (no /Metadata → null); we assert only
            // that it does not throw/hang, not non-null.
            ["Xmp"] = async (path, ct) =>
                await new PdfPigXmpService(NullLogger<PdfPigXmpService>.Instance).ReadAsync(path, ct),
            ["Preflight"] = async (path, ct) =>
            {
                var result = await NewPreflightService().PreflightAsync(path, ct);
                result.Should().NotBeNull();
            },
        };

    private static PdfPigPreflightService NewPreflightService() => new(
        new PdfPigFontService(NullLogger<PdfPigFontService>.Instance),
        new PdfPigSanitizationService(NullLogger<PdfPigSanitizationService>.Instance),
        new PdfPigOutputIntentService(NullLogger<PdfPigOutputIntentService>.Instance),
        new PdfPigLinkService(NullLogger<PdfPigLinkService>.Instance),
        NullLogger<PdfPigPreflightService>.Instance);

    private static byte[] GetFixtureBytes(string fixtureName)
    {
        foreach (var (name, bytes) in MalformedPdfCorpus.All())
        {
            if (string.Equals(name, fixtureName, StringComparison.Ordinal))
            {
                return bytes;
            }
        }

        throw new InvalidOperationException($"Unknown corpus fixture '{fixtureName}'.");
    }

    private async Task<string> WriteFixtureAsync(string fixtureName, byte[] bytes)
    {
        // Distinct file name per (fixture) so the matrix can run in parallel without colliding on disk.
        string path = Path.Combine(_tmpDir, $"{fixtureName}.pdf");
        await File.WriteAllBytesAsync(path, bytes, default);
        return path;
    }
}
