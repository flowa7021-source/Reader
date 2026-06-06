using System.Security.Cryptography;
using FluentAssertions;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Round-trip integration tests for <see cref="PdfPigViewerPreferencesService"/>: write
/// <c>/PageLayout</c> / <c>/PageMode</c> / <c>/ViewerPreferences</c> into the 10-page asset, then read
/// them back through the same production service and assert fidelity. Pure managed PdfPig — no native
/// runtime — so no Slow trait (mirrors <see cref="PdfPageLabelServiceTests"/>).
/// </summary>
public sealed class PdfViewerPreferencesServiceTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly PdfPigViewerPreferencesService _service = new(NullLogger<PdfPigViewerPreferencesService>.Instance);

    public PdfViewerPreferencesServiceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-viewerprefs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
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

    [Fact]
    public async Task Write_PageLayout_RoundTrips()
    {
        string target = TargetPath();
        var prefs = PdfViewerPreferences.Default with { PageLayout = PdfPageLayout.TwoPageLeft };

        await _service.WriteAsync(Asset, target, prefs, default);

        (await _service.ReadAsync(target, default)).Should().Be(prefs);
    }

    [Fact]
    public async Task Write_PageMode_RoundTrips()
    {
        string target = TargetPath();
        var prefs = PdfViewerPreferences.Default with { PageMode = PdfPageMode.UseOutlines };

        await _service.WriteAsync(Asset, target, prefs, default);

        (await _service.ReadAsync(target, default)).Should().Be(prefs);
    }

    [Theory]
    [InlineData(PdfPageLayout.SinglePage)]
    [InlineData(PdfPageLayout.OneColumn)]
    [InlineData(PdfPageLayout.TwoColumnLeft)]
    [InlineData(PdfPageLayout.TwoColumnRight)]
    [InlineData(PdfPageLayout.TwoPageLeft)]
    [InlineData(PdfPageLayout.TwoPageRight)]
    public async Task Write_EachPageLayout_RoundTrips(PdfPageLayout layout)
    {
        string target = TargetPath();
        var prefs = PdfViewerPreferences.Default with { PageLayout = layout };

        await _service.WriteAsync(Asset, target, prefs, default);

        (await _service.ReadAsync(target, default)).PageLayout.Should().Be(layout);
    }

    [Theory]
    [InlineData(PdfPageMode.UseNone)]
    [InlineData(PdfPageMode.UseOutlines)]
    [InlineData(PdfPageMode.UseThumbs)]
    [InlineData(PdfPageMode.FullScreen)]
    [InlineData(PdfPageMode.UseOC)]
    [InlineData(PdfPageMode.UseAttachments)]
    public async Task Write_EachPageMode_RoundTrips(PdfPageMode mode)
    {
        string target = TargetPath();
        var prefs = PdfViewerPreferences.Default with { PageMode = mode };

        await _service.WriteAsync(Asset, target, prefs, default);

        (await _service.ReadAsync(target, default)).PageMode.Should().Be(mode);
    }

    [Theory]
    [InlineData("HideToolbar")]
    [InlineData("HideMenubar")]
    [InlineData("FitWindow")]
    [InlineData("CenterWindow")]
    [InlineData("DisplayDocTitle")]
    public async Task Write_EachBooleanFlag_RoundTrips(string flag)
    {
        string target = TargetPath();
        var prefs = WithFlag(PdfViewerPreferences.Default, flag);

        await _service.WriteAsync(Asset, target, prefs, default);

        (await _service.ReadAsync(target, default)).Should().Be(prefs);
    }

    [Fact]
    public async Task Write_KitchenSink_RoundTripsAllFields()
    {
        string target = TargetPath();
        var prefs = new PdfViewerPreferences(
            PdfPageLayout.TwoColumnRight, PdfPageMode.UseOC, true, true, true, true, true);

        await _service.WriteAsync(Asset, target, prefs, default);

        (await _service.ReadAsync(target, default)).Should().Be(prefs);
    }

    [Fact]
    public async Task Write_Default_RoundTripsToDefault()
    {
        string target = TargetPath();

        await _service.WriteAsync(Asset, target, PdfViewerPreferences.Default, default);

        (await _service.ReadAsync(target, default)).Should().Be(PdfViewerPreferences.Default);
        PageCount(target).Should().Be(10, "writing default preferences must not corrupt the document");
    }

    [Fact]
    public async Task Write_Default_AfterSet_ClearsPreferences()
    {
        string seeded = TargetPath();
        var prefs = new PdfViewerPreferences(
            PdfPageLayout.TwoPageRight, PdfPageMode.UseOutlines, true, false, true, false, true);
        await _service.WriteAsync(Asset, seeded, prefs, default);
        (await _service.ReadAsync(seeded, default)).Should().Be(prefs);

        string cleared = TargetPath();
        await _service.WriteAsync(seeded, cleared, PdfViewerPreferences.Default, default);

        (await _service.ReadAsync(cleared, default)).Should().Be(PdfViewerPreferences.Default);
    }

    [Fact]
    public async Task Read_PdfWithoutPreferences_ReturnsDefault()
    {
        (await _service.ReadAsync(Asset, default)).Should().Be(PdfViewerPreferences.Default);
    }

    [Fact]
    public async Task Write_PreservesPageCount()
    {
        string target = TargetPath();

        await _service.WriteAsync(
            Asset, target, PdfViewerPreferences.Default with { PageMode = PdfPageMode.UseThumbs }, default);

        PageCount(target).Should().Be(PageCount(Asset));
    }

    [Fact]
    public async Task Write_DoesNotMutateSource()
    {
        string target = TargetPath();
        string before = Sha256(Asset);

        await _service.WriteAsync(
            Asset, target, PdfViewerPreferences.Default with { PageLayout = PdfPageLayout.TwoColumnLeft }, default);

        Sha256(Asset).Should().Be(before, "writer must not touch the source file");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Write_BlankSource_Throws(string blank)
    {
        var act = () => _service.WriteAsync(blank, TargetPath(), PdfViewerPreferences.Default, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Write_BlankTarget_Throws(string blank)
    {
        var act = () => _service.WriteAsync(Asset, blank, PdfViewerPreferences.Default, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Write_NullPrefs_Throws()
    {
        var act = () => _service.WriteAsync(Asset, TargetPath(), null!, default);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Read_BlankPath_Throws()
    {
        var act = () => _service.ReadAsync("  ", default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static PdfViewerPreferences WithFlag(PdfViewerPreferences prefs, string flag) => flag switch
    {
        "HideToolbar" => prefs with { HideToolbar = true },
        "HideMenubar" => prefs with { HideMenubar = true },
        "FitWindow" => prefs with { FitWindow = true },
        "CenterWindow" => prefs with { CenterWindow = true },
        "DisplayDocTitle" => prefs with { DisplayDocTitle = true },
        _ => throw new ArgumentOutOfRangeException(nameof(flag), flag, "Unknown flag."),
    };

    private string TargetPath() => Path.Combine(_tmpDir, "out-" + Guid.NewGuid().ToString("N") + ".pdf");

    private static int PageCount(string path)
    {
        using var doc = PdfPigDocument.Open(path);
        return doc.NumberOfPages;
    }

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

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
