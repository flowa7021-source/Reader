using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Tests for <see cref="PdfPigPreflightService"/> — the read-only preflight orchestrator that
/// <b>composes</b> the four existing PDF inspectors (<see cref="IPdfFontService"/>,
/// <see cref="IPdfSanitizationService"/>, <see cref="IPdfOutputIntentService"/>,
/// <see cref="IPdfLinkService"/>) and adds a PdfPig structural pass into one
/// <see cref="PdfPreflightReport"/>. Composition is verified by substituting the four inspectors with
/// canned returns (NSubstitute) while pointing the structural read at a real on-disk path; the
/// structural pass is verified against the shared 10-page text asset. Pure managed PdfPig — no native
/// runtime — so no Slow trait (mirrors <see cref="PdfFontServiceTests"/> / <see cref="PdfOutputIntentServiceTests"/>).
/// </summary>
public sealed class PdfPreflightServiceTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly IPdfFontService _fonts = Substitute.For<IPdfFontService>();
    private readonly IPdfSanitizationService _sanitization = Substitute.For<IPdfSanitizationService>();
    private readonly IPdfOutputIntentService _outputIntents = Substitute.For<IPdfOutputIntentService>();
    private readonly IPdfLinkService _links = Substitute.For<IPdfLinkService>();
    private readonly PdfPigPreflightService _service;

    public PdfPreflightServiceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);

        // Default canned returns: empty everywhere. Individual tests override what they care about.
        _fonts.ListFontsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PdfFontInfo>>([]));
        _sanitization.ScanAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PdfSanitizationReport([], HasJavaScriptOpenAction: false, HasDocumentAdditionalActions: false)));
        _outputIntents.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PdfOutputIntent>>([]));
        _links.ListLinksAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PdfLinkAnnotation>>([]));

        _service = new PdfPigPreflightService(
            _fonts, _sanitization, _outputIntents, _links, NullLogger<PdfPigPreflightService>.Instance);
    }

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

    // --- Constructor guards ----------------------------------------------------------------------

    [Fact]
    public void Ctor_NullDependency_Throws()
    {
        var act = () => new PdfPigPreflightService(
            null!, _sanitization, _outputIntents, _links, NullLogger<PdfPigPreflightService>.Instance);

        act.Should().Throw<ArgumentNullException>();
    }

    // --- Composition (substituted inspectors, structural read on a missing path) -----------------

    [Fact]
    public async Task Preflight_FontList_ProjectsFontAndNonEmbeddedCounts()
    {
        _fonts.ListFontsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PdfFontInfo>>(
            [
                new PdfFontInfo("Embedded", "TrueType", IsEmbedded: true),
                new PdfFontInfo("NonEmbeddedA", "Type1", IsEmbedded: false),
                new PdfFontInfo("NonEmbeddedB", "Type1", IsEmbedded: false),
            ]));

        var report = await _service.PreflightAsync(MissingPath, default);

        report.FontCount.Should().Be(3);
        report.NonEmbeddedFontCount.Should().Be(2);
    }

    [Fact]
    public async Task Preflight_NoFonts_CountsAreZero()
    {
        var report = await _service.PreflightAsync(MissingPath, default);

        report.FontCount.Should().Be(0);
        report.NonEmbeddedFontCount.Should().Be(0);
    }

    [Fact]
    public async Task Preflight_ScanWithJavaScript_SetsHasJavaScriptOrActionsTrue()
    {
        _sanitization.ScanAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PdfSanitizationReport(
                ["doc-js"], HasJavaScriptOpenAction: true, HasDocumentAdditionalActions: false)));

        var report = await _service.PreflightAsync(MissingPath, default);

        report.HasJavaScriptOrActions.Should().BeTrue();
    }

    [Fact]
    public async Task Preflight_CleanScan_SetsHasJavaScriptOrActionsFalse()
    {
        var report = await _service.PreflightAsync(MissingPath, default);

        report.HasJavaScriptOrActions.Should().BeFalse();
    }

    [Fact]
    public async Task Preflight_OutputIntents_ProjectsCountAndIccFlag()
    {
        _outputIntents.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PdfOutputIntent>>(
            [
                new PdfOutputIntent("GTS_PDFX", "FOGRA39", null, null, null, HasIccProfile: true),
                new PdfOutputIntent("GTS_PDFA1", "sRGB", null, null, null, HasIccProfile: false),
            ]));

        var report = await _service.PreflightAsync(MissingPath, default);

        report.OutputIntentCount.Should().Be(2);
        report.HasIccOutputIntent.Should().BeTrue();
    }

    [Fact]
    public async Task Preflight_OutputIntentsWithoutIcc_HasIccOutputIntentFalse()
    {
        _outputIntents.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PdfOutputIntent>>(
            [
                new PdfOutputIntent("GTS_PDFA1", "sRGB", null, null, null, HasIccProfile: false),
            ]));

        var report = await _service.PreflightAsync(MissingPath, default);

        report.OutputIntentCount.Should().Be(1);
        report.HasIccOutputIntent.Should().BeFalse();
    }

    [Fact]
    public async Task Preflight_Links_ProjectsLinkCount()
    {
        _links.ListLinksAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PdfLinkAnnotation>>(
            [
                new PdfLinkAnnotation(0, "https://example.com", null),
                new PdfLinkAnnotation(1, null, 2),
                new PdfLinkAnnotation(2, "https://example.org", null),
            ]));

        var report = await _service.PreflightAsync(MissingPath, default);

        report.LinkCount.Should().Be(3);
    }

    [Fact]
    public async Task Preflight_AllInspectorsConsultedOnce_WithSamePath()
    {
        string path = MissingPath;

        var report = await _service.PreflightAsync(path, default);

        await _fonts.Received(1).ListFontsAsync(path, Arg.Any<CancellationToken>());
        await _sanitization.Received(1).ScanAsync(path, Arg.Any<CancellationToken>());
        await _outputIntents.Received(1).ListAsync(path, Arg.Any<CancellationToken>());
        await _links.Received(1).ListLinksAsync(path, Arg.Any<CancellationToken>());
        report.Should().NotBeNull();
    }

    // --- Structural pass (real 10-page text asset; inspectors stubbed empty) ----------------------

    [Fact]
    public async Task Preflight_RealAsset_ReportsTenPages()
    {
        var report = await _service.PreflightAsync(Asset, default);

        report.PageCount.Should().Be(10);
    }

    [Fact]
    public async Task Preflight_RealAsset_HasExtractableTextTrue()
    {
        var report = await _service.PreflightAsync(Asset, default);

        report.HasExtractableText.Should().BeTrue("the shared asset is a text PDF, not an image-only scan");
    }

    [Fact]
    public async Task Preflight_RealAsset_PdfVersionStartsWithOnePointSomething()
    {
        var report = await _service.PreflightAsync(Asset, default);

        report.PdfVersion.Should().NotBeNullOrEmpty();
        report.PdfVersion.Should().StartWith("1.");
    }

    [Fact]
    public async Task Preflight_RealAsset_IsNotEncrypted()
    {
        var report = await _service.PreflightAsync(Asset, default);

        report.IsEncrypted.Should().BeFalse();
    }

    // --- Best-effort structural read -------------------------------------------------------------

    [Fact]
    public async Task Preflight_MissingFile_DoesNotThrow_StructuralDefaults()
    {
        var report = await _service.PreflightAsync(MissingPath, default);

        report.PageCount.Should().Be(0);
        report.PdfVersion.Should().BeEmpty();
        report.IsEncrypted.Should().BeFalse();
        report.HasExtractableText.Should().BeFalse();
    }

    [Fact]
    public async Task Preflight_CorruptFile_DoesNotThrow_StructuralDefaults()
    {
        string path = Path.Combine(_tmpDir, "corrupt.pdf");
        await File.WriteAllBytesAsync(path, "this is not a valid PDF at all"u8.ToArray());

        var report = await _service.PreflightAsync(path, default);

        report.PageCount.Should().Be(0);
        report.PdfVersion.Should().BeEmpty();
        report.HasExtractableText.Should().BeFalse();
    }

    [Fact]
    public async Task Preflight_CorruptFile_StillReturnsComposedInspectorFields()
    {
        // Even when the structural read fails, the composed fields from the (stubbed) inspectors survive.
        _fonts.ListFontsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PdfFontInfo>>(
            [
                new PdfFontInfo("NonEmbedded", "Type1", IsEmbedded: false),
            ]));
        string path = Path.Combine(_tmpDir, "corrupt2.pdf");
        await File.WriteAllBytesAsync(path, "%PDF-broken"u8.ToArray());

        var report = await _service.PreflightAsync(path, default);

        report.PageCount.Should().Be(0, "structural read failed");
        report.FontCount.Should().Be(1, "the composed font field still comes through");
        report.NonEmbeddedFontCount.Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Preflight_BlankPath_Throws(string blank)
    {
        var act = () => _service.PreflightAsync(blank, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // --- Cancellation ----------------------------------------------------------------------------

    [Fact]
    public async Task Preflight_CancelledToken_PropagatesFromInspector()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _fonts.ListFontsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromCanceled<IReadOnlyList<PdfFontInfo>>(cts.Token));

        var act = () => _service.PreflightAsync(Asset, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Preflight_CancelledToken_PropagatesFromStructuralRead()
    {
        // Inspectors complete synchronously (cancellation not observed there); the cancelled token must
        // still surface from the Task.Run-offloaded structural read against the real asset.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => _service.PreflightAsync(Asset, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // --- Asset resolution (mirrors PdfFontServiceTests / PdfOutputIntentServiceTests) ------------

    private static string MissingPath => Path.Combine(Path.GetTempPath(), "foliant-preflight-missing-" + Guid.NewGuid().ToString("N") + ".pdf");

    private static string Asset => Path.Combine(ResolveAssetsDir(), "pdf-text-en-10p.pdf");

    private static string ResolveAssetsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Foliant.sln")))
            {
                return Path.Combine(dir.FullName, "tests", "assets");
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root (Foliant.sln).");
    }
}
