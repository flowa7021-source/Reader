using System.Security.Cryptography;
using FluentAssertions;
using Foliant.Domain;
using Foliant.Engines.Pdf;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Round-trip integration tests for <see cref="PdfPigOutlineWriter"/>: write a /Outlines tree into
/// the 10-page asset, then read it back with the production <see cref="PdfPigOutlineReader"/> and
/// assert title/page/depth fidelity. Pure managed PdfPig — no native runtime — so no Slow trait
/// (mirrors <see cref="PdfPigOutlineReaderTests"/>).
/// </summary>
public sealed class PdfOutlineWriterTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly PdfPigOutlineWriter _writer = new(NullLogger<PdfPigOutlineWriter>.Instance);
    private readonly PdfPigOutlineReader _reader = new(NullLogger<PdfPigOutlineReader>.Instance);

    public PdfOutlineWriterTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-outline-writer-" + Guid.NewGuid().ToString("N"));
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
    public async Task WriteOutline_Flat_RoundTripsTitlesAndPages()
    {
        string target = TargetPath();
        IReadOnlyList<DocumentOutlineEntry> entries =
        [
            new(0, "Chapter 1", 0),
            new(3, "Chapter 2", 0),
            new(6, "Chapter 3", 0),
        ];

        await _writer.WriteOutlineAsync(Asset, target, entries, default);

        var read = await _reader.ReadAsync(target, default);
        read.Select(e => (e.Title, e.PageIndex, e.Depth)).Should().Equal(
            ("Chapter 1", 0, 0),
            ("Chapter 2", 3, 0),
            ("Chapter 3", 6, 0));
    }

    [Fact]
    public async Task WriteOutline_Nested_RoundTripsDepth()
    {
        string target = TargetPath();
        // Depth 0/1/1/0 — Chapter 1 with two sections, then Chapter 2.
        IReadOnlyList<DocumentOutlineEntry> entries =
        [
            new(0, "Chapter 1", 0),
            new(1, "Section 1.1", 1),
            new(2, "Section 1.2", 1),
            new(3, "Chapter 2", 0),
        ];

        await _writer.WriteOutlineAsync(Asset, target, entries, default);

        var read = await _reader.ReadAsync(target, default);
        read.Select(e => (e.Title, e.Depth)).Should().Equal(
            ("Chapter 1", 0),
            ("Section 1.1", 1),
            ("Section 1.2", 1),
            ("Chapter 2", 0));
    }

    [Fact]
    public async Task WriteOutline_Empty_ProducesValidPdfWithoutOutline()
    {
        string target = TargetPath();

        await _writer.WriteOutlineAsync(Asset, target, [], default);

        var read = await _reader.ReadAsync(target, default);
        read.Should().BeEmpty();
        PageCount(target).Should().Be(10, "an empty outline must not corrupt the document");
    }

    [Fact]
    public async Task WriteOutline_PreservesPageCount()
    {
        string target = TargetPath();
        IReadOnlyList<DocumentOutlineEntry> entries = [new(0, "Only", 0)];

        await _writer.WriteOutlineAsync(Asset, target, entries, default);

        PageCount(target).Should().Be(PageCount(Asset));
    }

    [Fact]
    public async Task WriteOutline_DoesNotMutateSource()
    {
        string target = TargetPath();
        string before = Sha256(Asset);
        IReadOnlyList<DocumentOutlineEntry> entries =
        [
            new(0, "A", 0),
            new(5, "B", 1),
        ];

        await _writer.WriteOutlineAsync(Asset, target, entries, default);

        Sha256(Asset).Should().Be(before, "writer must not touch the source file");
    }

    [Fact]
    public async Task WriteOutline_UnicodeTitle_RoundTripsLossless()
    {
        string target = TargetPath();
        IReadOnlyList<DocumentOutlineEntry> entries =
        [
            new(0, "Глава первая — введение", 0),
            new(2, "Раздел 1.1: обзор", 1),
        ];

        await _writer.WriteOutlineAsync(Asset, target, entries, default);

        var read = await _reader.ReadAsync(target, default);
        read.Select(e => e.Title).Should().Equal(
            "Глава первая — введение",
            "Раздел 1.1: обзор");
    }

    [Fact]
    public async Task WriteOutline_PageIndexOutOfRange_ClampsIntoDocument()
    {
        string target = TargetPath();
        // 10-page asset: -5 clamps to first page (index 0), 999 clamps to last (index 9).
        IReadOnlyList<DocumentOutlineEntry> entries =
        [
            new(-5, "Underflow", 0),
            new(999, "Overflow", 0),
        ];

        await _writer.WriteOutlineAsync(Asset, target, entries, default);

        var read = await _reader.ReadAsync(target, default);
        read.Select(e => (e.Title, e.PageIndex)).Should().Equal(
            ("Underflow", 0),
            ("Overflow", 9));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WriteOutline_BlankSource_Throws(string blank)
    {
        var act = () => _writer.WriteOutlineAsync(blank, TargetPath(), [], default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WriteOutline_BlankTarget_Throws(string blank)
    {
        var act = () => _writer.WriteOutlineAsync(Asset, blank, [], default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task WriteOutline_NullEntries_Throws()
    {
        var act = () => _writer.WriteOutlineAsync(Asset, TargetPath(), null!, default);

        await act.Should().ThrowAsync<ArgumentNullException>();
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
