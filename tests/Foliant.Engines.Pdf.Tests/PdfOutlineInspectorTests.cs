using System.Diagnostics;
using System.Globalization;
using System.Text;
using FluentAssertions;
using Foliant.Domain;
using Foliant.Engines.Pdf;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Round-trip tests for the rich /Outlines reader: write a rich outline into the 10-page asset with
/// the production <see cref="PdfPigOutlineWriter"/>, then read it back with
/// <see cref="PdfPigOutlineInspector"/> and assert every attribute survives — titles, depths,
/// destination modes, bold/italic, colour, open/closed, page indices. Cross-verifies both sides
/// (writer + cos reader). Pure managed PdfPig — no native runtime — so no Slow trait.
/// </summary>
public sealed class PdfOutlineInspectorTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly PdfPigOutlineWriter _writer = new(NullLogger<PdfPigOutlineWriter>.Instance);
    private readonly PdfPigOutlineInspector _inspector = new(NullLogger<PdfPigOutlineInspector>.Instance);

    public PdfOutlineInspectorTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-outline-inspector-" + Guid.NewGuid().ToString("N"));
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

    // ---- Titles / pages / order -------------------------------------------------------------------

    [Fact]
    public async Task ReadRich_Flat_RoundTripsTitlesPagesAndOrder()
    {
        var read = await RoundTrip(
        [
            new(0, "Chapter 1", 0),
            new(3, "Chapter 2", 0),
            new(6, "Chapter 3", 0),
        ]);

        read.Select(e => (e.Title, e.PageIndex, e.Depth)).Should().Equal(
            ("Chapter 1", 0, 0),
            ("Chapter 2", 3, 0),
            ("Chapter 3", 6, 0));
    }

    [Fact]
    public async Task ReadRich_UnicodeTitle_RoundTripsLossless()
    {
        var read = await RoundTrip(
        [
            new(0, "Глава первая — введение", 0),
            new(2, "Раздел 1.1: обзор", 1),
        ]);

        read.Select(e => e.Title).Should().Equal(
            "Глава первая — введение",
            "Раздел 1.1: обзор");
    }

    // ---- Nesting / depth --------------------------------------------------------------------------

    [Fact]
    public async Task ReadRich_Nested_RoundTripsDepthInPreOrder()
    {
        var read = await RoundTrip(
        [
            new(0, "Chapter 1", 0),
            new(1, "Section 1.1", 1),
            new(2, "Section 1.2", 1),
            new(3, "Chapter 2", 0),
        ]);

        read.Select(e => (e.Title, e.Depth)).Should().Equal(
            ("Chapter 1", 0),
            ("Section 1.1", 1),
            ("Section 1.2", 1),
            ("Chapter 2", 0));
    }

    [Fact]
    public async Task ReadRich_DeepNesting_RoundTripsThreeLevels()
    {
        var read = await RoundTrip(
        [
            new(0, "Part I", 0),
            new(1, "Chapter 1", 1),
            new(2, "Section 1.1", 2),
            new(3, "Sub 1.1.1", 3),
            new(4, "Chapter 2", 1),
            new(5, "Part II", 0),
        ]);

        read.Select(e => (e.Title, e.Depth)).Should().Equal(
            ("Part I", 0),
            ("Chapter 1", 1),
            ("Section 1.1", 2),
            ("Sub 1.1.1", 3),
            ("Chapter 2", 1),
            ("Part II", 0));
    }

    [Fact]
    public async Task ReadRich_NestedPageIndices_RoundTrip()
    {
        var read = await RoundTrip(
        [
            new(0, "A", 0),
            new(5, "A.1", 1),
            new(9, "A.1.1", 2),
        ]);

        read.Select(e => e.PageIndex).Should().Equal(0, 5, 9);
    }

    // ---- Destination modes ------------------------------------------------------------------------

    [Theory]
    [InlineData(OutlineDestinationMode.FitPage)]
    [InlineData(OutlineDestinationMode.FitWidth)]
    [InlineData(OutlineDestinationMode.FitHeight)]
    [InlineData(OutlineDestinationMode.InheritZoom)]
    public async Task ReadRich_DestinationMode_RoundTrips(OutlineDestinationMode mode)
    {
        var read = await RoundTrip([new(2, "Target", 0, Destination: mode)]);

        read.Should().ContainSingle();
        read[0].Destination.Should().Be(mode);
        read[0].PageIndex.Should().Be(2);
    }

    [Fact]
    public async Task ReadRich_MixedDestinationModes_EachRoundTrips()
    {
        var read = await RoundTrip(
        [
            new(0, "Fit", 0, Destination: OutlineDestinationMode.FitPage),
            new(1, "FitH", 0, Destination: OutlineDestinationMode.FitWidth),
            new(2, "FitV", 0, Destination: OutlineDestinationMode.FitHeight),
            new(3, "XYZ", 0, Destination: OutlineDestinationMode.InheritZoom),
        ]);

        read.Select(e => (e.Title, e.Destination)).Should().Equal(
            ("Fit", OutlineDestinationMode.FitPage),
            ("FitH", OutlineDestinationMode.FitWidth),
            ("FitV", OutlineDestinationMode.FitHeight),
            ("XYZ", OutlineDestinationMode.InheritZoom));
    }

    // ---- Style: bold / italic ---------------------------------------------------------------------

    [Fact]
    public async Task ReadRich_Bold_RoundTrips()
    {
        var read = await RoundTrip([new(0, "Bold", 0, IsBold: true)]);

        read[0].IsBold.Should().BeTrue();
        read[0].IsItalic.Should().BeFalse();
    }

    [Fact]
    public async Task ReadRich_Italic_RoundTrips()
    {
        var read = await RoundTrip([new(0, "Italic", 0, IsItalic: true)]);

        read[0].IsItalic.Should().BeTrue();
        read[0].IsBold.Should().BeFalse();
    }

    [Fact]
    public async Task ReadRich_BoldAndItalic_RoundTrips()
    {
        var read = await RoundTrip([new(0, "BoldItalic", 0, IsBold: true, IsItalic: true)]);

        read[0].IsBold.Should().BeTrue();
        read[0].IsItalic.Should().BeTrue();
    }

    [Fact]
    public async Task ReadRich_PlainStyle_HasNoFlags()
    {
        var read = await RoundTrip([new(0, "Plain", 0)]);

        read[0].IsBold.Should().BeFalse();
        read[0].IsItalic.Should().BeFalse();
    }

    // ---- Colour -----------------------------------------------------------------------------------

    [Fact]
    public async Task ReadRich_Colour_RoundTrips()
    {
        var read = await RoundTrip([new(0, "Red", 0, Color: new OutlineColor(1.0, 0.0, 0.0))]);

        read[0].Color.Should().NotBeNull();
        read[0].Color!.Value.Red.Should().BeApproximately(1.0, 1e-4);
        read[0].Color!.Value.Green.Should().BeApproximately(0.0, 1e-4);
        read[0].Color!.Value.Blue.Should().BeApproximately(0.0, 1e-4);
    }

    [Fact]
    public async Task ReadRich_FractionalColour_RoundTripsWithinTolerance()
    {
        var read = await RoundTrip([new(0, "Teal", 0, Color: new OutlineColor(0.1234, 0.5, 0.75))]);

        read[0].Color.Should().NotBeNull();
        read[0].Color!.Value.Red.Should().BeApproximately(0.1234, 1e-3);
        read[0].Color!.Value.Green.Should().BeApproximately(0.5, 1e-3);
        read[0].Color!.Value.Blue.Should().BeApproximately(0.75, 1e-3);
    }

    [Fact]
    public async Task ReadRich_NoColour_ReadsBackNull()
    {
        var read = await RoundTrip([new(0, "Default", 0)]);

        read[0].Color.Should().BeNull();
    }

    // ---- Open / closed ----------------------------------------------------------------------------

    [Fact]
    public async Task ReadRich_CollapsedParent_ReadsBackClosed()
    {
        var read = await RoundTrip(
        [
            new(0, "Collapsed", 0, IsOpen: false),
            new(1, "Child", 1),
        ]);

        read.Should().HaveCount(2);
        read[0].Title.Should().Be("Collapsed");
        read[0].IsOpen.Should().BeFalse("a negative /Count must read back as closed");
        read[1].Title.Should().Be("Child");
    }

    [Fact]
    public async Task ReadRich_ExpandedParent_ReadsBackOpen()
    {
        var read = await RoundTrip(
        [
            new(0, "Expanded", 0, IsOpen: true),
            new(1, "Child", 1),
        ]);

        read[0].IsOpen.Should().BeTrue();
    }

    [Fact]
    public async Task ReadRich_Leaf_IsOpenTrue()
    {
        // A childless node omits /Count entirely -> reader defaults to open.
        var read = await RoundTrip([new(0, "Leaf", 0, IsOpen: false)]);

        read.Should().ContainSingle();
        read[0].IsOpen.Should().BeTrue("a leaf has no /Count, so open is the default");
    }

    // ---- Full-richness round-trip -----------------------------------------------------------------

    [Fact]
    public async Task ReadRich_AllAttributesCombined_RoundTrip()
    {
        var read = await RoundTrip(
        [
            new(0, "Parent", 0, OutlineDestinationMode.FitWidth, IsBold: true, IsItalic: false,
                Color: new OutlineColor(0.2, 0.4, 0.6), IsOpen: false),
            new(4, "Child", 1, OutlineDestinationMode.InheritZoom, IsBold: false, IsItalic: true,
                Color: new OutlineColor(0.0, 1.0, 0.0)),
        ]);

        read.Should().HaveCount(2);

        var parent = read[0];
        parent.Title.Should().Be("Parent");
        parent.Depth.Should().Be(0);
        parent.PageIndex.Should().Be(0);
        parent.Destination.Should().Be(OutlineDestinationMode.FitWidth);
        parent.IsBold.Should().BeTrue();
        parent.IsItalic.Should().BeFalse();
        parent.IsOpen.Should().BeFalse();
        parent.Color!.Value.Red.Should().BeApproximately(0.2, 1e-3);

        var child = read[1];
        child.Title.Should().Be("Child");
        child.Depth.Should().Be(1);
        child.PageIndex.Should().Be(4);
        child.Destination.Should().Be(OutlineDestinationMode.InheritZoom);
        child.IsItalic.Should().BeTrue();
        child.Color!.Value.Green.Should().BeApproximately(1.0, 1e-3);
    }

    [Fact]
    public async Task ReadRich_PlainOutline_AllDefaults()
    {
        var read = await RoundTrip(
        [
            new(0, "One", 0),
            new(1, "Two", 0),
        ]);

        read.Should().AllSatisfy(e =>
        {
            e.Destination.Should().Be(OutlineDestinationMode.FitPage);
            e.IsBold.Should().BeFalse();
            e.IsItalic.Should().BeFalse();
            e.Color.Should().BeNull();
            e.IsOpen.Should().BeTrue();
        });
    }

    // ---- Empty / missing / errors -----------------------------------------------------------------

    [Fact]
    public async Task ReadRich_NoOutline_ReturnsEmpty()
    {
        // The bare asset has no /Outlines at all.
        var read = await _inspector.ReadRichAsync(Asset, default);

        read.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadRich_EmptyOutlineWritten_ReturnsEmpty()
    {
        string target = TargetPath();
        await _writer.WriteOutlineAsync(Asset, target, [], default);

        var read = await _inspector.ReadRichAsync(target, default);

        read.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadRich_MissingFile_ReturnsEmpty_NotThrow()
    {
        var read = await _inspector.ReadRichAsync(Path.Combine(_tmpDir, "does-not-exist.pdf"), default);

        read.Should().BeEmpty("inspection is best-effort and must not throw on a missing file");
    }

    [Fact]
    public async Task ReadRich_CorruptFile_ReturnsEmpty_NotThrow()
    {
        string path = Path.Combine(_tmpDir, "corrupt.pdf");
        await File.WriteAllBytesAsync(path, "%PDF-1.7 this is not a real pdf"u8.ToArray(), default);

        var read = await _inspector.ReadRichAsync(path, default);

        read.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadRich_CyclicOutlineChain_CompletesWithoutHang()
    {
        // Hand-built malformed PDF whose /Outlines /Next chain cycles (A -> B -> A). Without the
        // visited-set / iteration cap the sibling walk would loop forever; with it the best-effort
        // reader returns a (possibly partial) list promptly and never hangs / overflows.
        string path = Path.Combine(_tmpDir, "cyclic-outline.pdf");
        await File.WriteAllBytesAsync(path, CyclicOutlinePdf(), default);

        var sw = Stopwatch.StartNew();
        var read = await _inspector.ReadRichAsync(path, default);
        sw.Stop();

        read.Should().NotBeNull("best-effort reader returns a list, never crashes/hangs on a cycle");
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30), "the cycle guard must terminate the walk");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReadRich_BlankPath_Throws(string blank)
    {
        var act = () => _inspector.ReadRichAsync(blank, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReadRich_Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => _inspector.ReadRichAsync(Asset, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ReadRich_NullLogger_CtorThrows()
    {
        var act = () => new PdfPigOutlineInspector(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    private async Task<IReadOnlyList<DocumentOutlineEntry>> RoundTrip(IReadOnlyList<DocumentOutlineEntry> entries)
    {
        string target = TargetPath();
        await _writer.WriteOutlineAsync(Asset, target, entries, default);
        return await _inspector.ReadRichAsync(target, default);
    }

    private string TargetPath() => Path.Combine(_tmpDir, "out-" + Guid.NewGuid().ToString("N") + ".pdf");

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

    /// <summary>
    /// Malformed PDF whose /Outlines sibling chain cycles. Objects: 1 = Catalog (/Outlines 5 0 R,
    /// /Pages 2 0 R), 2 = /Pages with one real leaf (3) so PdfPig opens the doc, 3 = a valid /Page,
    /// 5 = /Outlines root (/First 6 0 R), 6 = item A (/Next 7 0 R), 7 = item B (/Next 6 0 R) — A→B→A is
    /// the /Next cycle the sibling walk must not follow forever.
    /// </summary>
    private static byte[] CyclicOutlinePdf()
    {
        var objects = new List<(int ObjNumber, string Body)>
        {
            (1, "1 0 obj\n<</Type/Catalog/Pages 2 0 R/Outlines 5 0 R>>\nendobj\n"),
            (2, "2 0 obj\n<</Type/Pages/Kids[3 0 R]/Count 1>>\nendobj\n"),
            (3, "3 0 obj\n<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]>>\nendobj\n"),

            (5, "5 0 obj\n<</Type/Outlines/First 6 0 R/Last 7 0 R/Count 2>>\nendobj\n"),

            // Item A → Item B → Item A: a two-node /Next cycle.
            (6, "6 0 obj\n<</Title(A)/Parent 5 0 R/Next 7 0 R/Dest[3 0 R /Fit]>>\nendobj\n"),
            (7, "7 0 obj\n<</Title(B)/Parent 5 0 R/Prev 6 0 R/Next 6 0 R/Dest[3 0 R /Fit]>>\nendobj\n"),
        };

        return Assemble(objects, rootObj: 1);
    }

    /// <summary>Serialises sparse, hand-numbered objects into a PDF with a classic cross-reference table
    /// and trailer. Object numbers may be non-contiguous; gaps are emitted as free ("f") xref entries so
    /// PdfPig still parses the table.</summary>
    private static byte[] Assemble(IReadOnlyList<(int ObjNumber, string Body)> objects, int rootObj)
    {
        var enc = Encoding.Latin1;
        int maxObj = 0;
        foreach (var (objNumber, _) in objects)
        {
            maxObj = Math.Max(maxObj, objNumber);
        }

        var offsets = new int[maxObj + 1]; // 1-based object numbers; 0 stays unused (free head)
        var present = new bool[maxObj + 1];

        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");
        foreach (var (objNumber, body) in objects)
        {
            offsets[objNumber] = enc.GetByteCount(sb.ToString());
            present[objNumber] = true;
            sb.Append(body);
        }

        int xrefStart = enc.GetByteCount(sb.ToString());
        AppendXref(sb, offsets, present, maxObj);
        sb.Append(CultureInfo.InvariantCulture,
            $"trailer\n<</Size {maxObj + 1}/Root {rootObj} 0 R>>\nstartxref\n{xrefStart}\n%%EOF\n");

        return enc.GetBytes(sb.ToString());
    }

    private static void AppendXref(StringBuilder sb, int[] offsets, bool[] present, int maxObj)
    {
        sb.Append("xref\n");
        sb.Append(CultureInfo.InvariantCulture, $"0 {maxObj + 1}\n");
        sb.Append("0000000000 65535 f \n"); // object 0 — free list head
        for (int obj = 1; obj <= maxObj; obj++)
        {
            sb.Append(present[obj]
                ? string.Create(CultureInfo.InvariantCulture, $"{offsets[obj]:D10} 00000 n \n")
                : "0000000000 00000 f \n"); // gap → free entry, keeps the subsection contiguous
        }
    }
}
