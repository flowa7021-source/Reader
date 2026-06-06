using System.Security.Cryptography;
using FluentAssertions;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Round-trip integration tests for <see cref="PdfPigPageLabelService"/>: write a /PageLabels
/// number-tree into the 10-page asset, then read it back through the same production service and
/// assert fidelity. Pure managed PdfPig — no native runtime — so no Slow trait (mirrors
/// <see cref="PdfOutlineWriterTests"/>).
/// </summary>
public sealed class PdfPageLabelServiceTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly PdfPigPageLabelService _service = new(NullLogger<PdfPigPageLabelService>.Instance);

    public PdfPageLabelServiceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-pagelabel-" + Guid.NewGuid().ToString("N"));
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
    public async Task Write_SingleArabicRange_RoundTrips()
    {
        string target = TargetPath();
        IReadOnlyList<PdfPageLabelRange> ranges = [PdfPageLabelRange.Create(0, PdfPageLabelStyle.Arabic)];

        await _service.WriteAsync(Asset, target, ranges, default);

        var read = await _service.ReadAsync(target, default);
        read.Should().Equal(ranges);
    }

    [Fact]
    public async Task Write_MixedRanges_RoundTripsAllFields()
    {
        string target = TargetPath();
        // Front matter (lower roman), body (decimal from 1), appendix (letter prefix, start 1).
        IReadOnlyList<PdfPageLabelRange> ranges =
        [
            PdfPageLabelRange.Create(0, PdfPageLabelStyle.LowerRoman),
            PdfPageLabelRange.Create(3, PdfPageLabelStyle.Arabic),
            PdfPageLabelRange.Create(7, PdfPageLabelStyle.UpperLetters, "App-"),
        ];

        await _service.WriteAsync(Asset, target, ranges, default);

        var read = await _service.ReadAsync(target, default);
        read.Should().Equal(ranges);
    }

    [Fact]
    public async Task Write_StartOffset_RoundTrips()
    {
        string target = TargetPath();
        IReadOnlyList<PdfPageLabelRange> ranges = [PdfPageLabelRange.Create(0, PdfPageLabelStyle.Arabic, start: 42)];

        await _service.WriteAsync(Asset, target, ranges, default);

        (await _service.ReadAsync(target, default)).Should().Equal(ranges);
    }

    [Fact]
    public async Task Write_AsciiPrefix_RoundTrips()
    {
        string target = TargetPath();
        IReadOnlyList<PdfPageLabelRange> ranges = [PdfPageLabelRange.Create(0, PdfPageLabelStyle.Arabic, "A-1=(")];

        await _service.WriteAsync(Asset, target, ranges, default);

        (await _service.ReadAsync(target, default)).Should().Equal(ranges);
    }

    [Fact]
    public async Task Write_UnicodePrefix_RoundTripsLossless()
    {
        string target = TargetPath();
        IReadOnlyList<PdfPageLabelRange> ranges =
        [
            PdfPageLabelRange.Create(0, PdfPageLabelStyle.Arabic, "Прил-"),
        ];

        await _service.WriteAsync(Asset, target, ranges, default);

        (await _service.ReadAsync(target, default)).Should().Equal(ranges);
    }

    [Fact]
    public async Task Write_UnsortedInput_ProducesSortedOutput()
    {
        string target = TargetPath();
        IReadOnlyList<PdfPageLabelRange> ranges =
        [
            PdfPageLabelRange.Create(5, PdfPageLabelStyle.Arabic),
            PdfPageLabelRange.Create(0, PdfPageLabelStyle.LowerRoman),
        ];

        await _service.WriteAsync(Asset, target, ranges, default);

        var read = await _service.ReadAsync(target, default);
        read.Select(r => r.StartPageIndex).Should().Equal(0, 5);
    }

    [Fact]
    public async Task Write_FormatsVisibleLabelsViaFormatter()
    {
        // End-to-end: write roman front matter + decimal body, read back, format visible labels.
        string target = TargetPath();
        IReadOnlyList<PdfPageLabelRange> ranges =
        [
            PdfPageLabelRange.Create(0, PdfPageLabelStyle.LowerRoman),
            PdfPageLabelRange.Create(3, PdfPageLabelStyle.Arabic),
        ];

        await _service.WriteAsync(Asset, target, ranges, default);
        var read = await _service.ReadAsync(target, default);

        PdfPageLabelFormatter.Format(read, 0).Should().Be("i");
        PdfPageLabelFormatter.Format(read, 2).Should().Be("iii");
        PdfPageLabelFormatter.Format(read, 3).Should().Be("1");
        PdfPageLabelFormatter.Format(read, 9).Should().Be("7");
    }

    [Fact]
    public async Task Write_Empty_DropsPageLabelsButKeepsValidPdf()
    {
        string seeded = TargetPath();
        await _service.WriteAsync(Asset, seeded, [PdfPageLabelRange.Create(0, PdfPageLabelStyle.Arabic)], default);
        (await _service.ReadAsync(seeded, default)).Should().NotBeEmpty();

        string cleared = TargetPath();
        await _service.WriteAsync(seeded, cleared, [], default);

        (await _service.ReadAsync(cleared, default)).Should().BeEmpty();
        PageCount(cleared).Should().Be(10, "clearing page labels must not corrupt the document");
    }

    [Fact]
    public async Task Write_PreservesPageCount()
    {
        string target = TargetPath();

        await _service.WriteAsync(Asset, target, [PdfPageLabelRange.Create(0, PdfPageLabelStyle.Arabic)], default);

        PageCount(target).Should().Be(PageCount(Asset));
    }

    [Fact]
    public async Task Write_DoesNotMutateSource()
    {
        string target = TargetPath();
        string before = Sha256(Asset);

        await _service.WriteAsync(Asset, target, [PdfPageLabelRange.Create(0, PdfPageLabelStyle.UpperRoman)], default);

        Sha256(Asset).Should().Be(before, "writer must not touch the source file");
    }

    [Fact]
    public async Task Write_DuplicateStartPageIndex_Throws()
    {
        IReadOnlyList<PdfPageLabelRange> ranges =
        [
            PdfPageLabelRange.Create(2, PdfPageLabelStyle.Arabic),
            PdfPageLabelRange.Create(2, PdfPageLabelStyle.UpperRoman),
        ];

        var act = () => _service.WriteAsync(Asset, TargetPath(), ranges, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Read_PdfWithoutPageLabels_ReturnsEmpty()
    {
        (await _service.ReadAsync(Asset, default)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Write_BlankSource_Throws(string blank)
    {
        var act = () => _service.WriteAsync(blank, TargetPath(), [], default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Write_BlankTarget_Throws(string blank)
    {
        var act = () => _service.WriteAsync(Asset, blank, [], default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Write_NullRanges_Throws()
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
