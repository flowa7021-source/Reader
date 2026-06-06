using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FluentAssertions;
using Foliant.Engines.Pdf;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Exercises <see cref="PdfPigInsertPagesService"/> using the committed 10-page text asset as the
/// source and a tiny 2-page builder doc as the inserted document. Pure-managed (PdfPig) — green
/// under the Linux unit filter, no PDFium needed.
///
/// Source asset pages start with "Page {N} of 10"; inserted pages start with "INSERTED A"/"INSERTED B".
/// Every output page therefore carries an assertable marker, so we can prove exactly which page
/// landed at which position after the insertion.
/// </summary>
public sealed class PdfPigInsertPagesServiceTests : IDisposable
{
    private const int SourcePageCount = 10;
    private const string InsertedA = "INSERTED A";
    private const string InsertedB = "INSERTED B";

    private readonly string _tmpDir;

    public PdfPigInsertPagesServiceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-insert-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
    }

    /// <summary>Cleans up the per-test temp directory.</summary>
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
    public async Task InsertAsync_AfterMiddlePage_PlacesInsertedPagesAndShiftsTail()
    {
        string insertDoc = WriteInsertDoc();
        string dst = Path.Combine(_tmpDir, "middle.pdf");

        // Insert after source index 2 (1-based page 3): output = src 1..3, INSERTED A/B, src 4..10.
        await new PdfPigInsertPagesService().InsertAsync(Asset, 2, insertDoc, dst, default);

        PageCount(dst).Should().Be(SourcePageCount + 2);
        PageMarkers(dst).Should().Equal(
            "Page 1 of 10", "Page 2 of 10", "Page 3 of 10",
            InsertedA, InsertedB,
            "Page 4 of 10", "Page 5 of 10", "Page 6 of 10", "Page 7 of 10", "Page 8 of 10", "Page 9 of 10", "Page 10 of 10");
    }

    [Fact]
    public async Task InsertAsync_AtStart_PlacesInsertedPagesBeforeAllSourcePages()
    {
        string insertDoc = WriteInsertDoc();
        string dst = Path.Combine(_tmpDir, "start.pdf");

        await new PdfPigInsertPagesService().InsertAsync(Asset, -1, insertDoc, dst, default);

        PageCount(dst).Should().Be(SourcePageCount + 2);
        PageMarkers(dst).Should().Equal(
            InsertedA, InsertedB,
            "Page 1 of 10", "Page 2 of 10", "Page 3 of 10", "Page 4 of 10", "Page 5 of 10",
            "Page 6 of 10", "Page 7 of 10", "Page 8 of 10", "Page 9 of 10", "Page 10 of 10");
    }

    [Fact]
    public async Task InsertAsync_AtEnd_AppendsInsertedPagesAfterAllSourcePages()
    {
        string insertDoc = WriteInsertDoc();
        string dst = Path.Combine(_tmpDir, "end.pdf");

        await new PdfPigInsertPagesService().InsertAsync(Asset, SourcePageCount - 1, insertDoc, dst, default);

        PageCount(dst).Should().Be(SourcePageCount + 2);
        PageMarkers(dst).Should().Equal(
            "Page 1 of 10", "Page 2 of 10", "Page 3 of 10", "Page 4 of 10", "Page 5 of 10",
            "Page 6 of 10", "Page 7 of 10", "Page 8 of 10", "Page 9 of 10", "Page 10 of 10",
            InsertedA, InsertedB);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(SourcePageCount)]
    public async Task InsertAsync_IndexOutOfRange_Throws(int insertAfterPageIndex)
    {
        string insertDoc = WriteInsertDoc();

        var act = () => new PdfPigInsertPagesService()
            .InsertAsync(Asset, insertAfterPageIndex, insertDoc, Path.Combine(_tmpDir, "oob.pdf"), default);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("", "insert.pdf", "out.pdf")]
    [InlineData("   ", "insert.pdf", "out.pdf")]
    [InlineData("source.pdf", "", "out.pdf")]
    [InlineData("source.pdf", "   ", "out.pdf")]
    [InlineData("source.pdf", "insert.pdf", "")]
    [InlineData("source.pdf", "insert.pdf", "   ")]
    public async Task InsertAsync_BlankPath_Throws(string source, string insert, string target)
    {
        var act = () => new PdfPigInsertPagesService().InsertAsync(source, 0, insert, target, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task InsertAsync_DoesNotMutateSourceFile()
    {
        // Copy the asset into the temp dir so we can hash it before/after as the real source path.
        string source = Path.Combine(_tmpDir, "source.pdf");
        File.Copy(Asset, source);
        string insertDoc = WriteInsertDoc();
        byte[] before = Sha256(source);

        await new PdfPigInsertPagesService().InsertAsync(source, 4, insertDoc, Path.Combine(_tmpDir, "out.pdf"), default);

        Sha256(source).Should().Equal(before);
    }

    [Fact]
    public async Task InsertAsync_SourceEqualsTarget_OverwritesAtomicallyWithInsertedPages()
    {
        string sourceAndTarget = Path.Combine(_tmpDir, "in-place.pdf");
        File.Copy(Asset, sourceAndTarget);
        string insertDoc = WriteInsertDoc();

        await new PdfPigInsertPagesService().InsertAsync(sourceAndTarget, 0, insertDoc, sourceAndTarget, default);

        PageCount(sourceAndTarget).Should().Be(SourcePageCount + 2);
        PageMarkers(sourceAndTarget).Take(3).Should().Equal("Page 1 of 10", InsertedA, InsertedB);
        Directory.EnumerateFiles(_tmpDir, ".*.tmp").Should().BeEmpty();
        Directory.EnumerateFiles(_tmpDir, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task InsertAsync_OutputReopensAsValidPdf()
    {
        string insertDoc = WriteInsertDoc();
        string dst = Path.Combine(_tmpDir, "valid.pdf");

        await new PdfPigInsertPagesService().InsertAsync(Asset, 5, insertDoc, dst, default);

        // Reopening without throwing + all pages enumerable proves a structurally valid PDF.
        using var doc = PdfPigDocument.Open(dst);
        doc.NumberOfPages.Should().Be(SourcePageCount + 2);
        doc.GetPages().Should().HaveCount(SourcePageCount + 2);
    }

    private static string Asset => Path.Combine(ResolveAssetsDir(), "pdf-text-en-10p.pdf");

    /// <summary>Builds a 2-page PDF whose pages carry the "INSERTED A"/"INSERTED B" markers.</summary>
    private string WriteInsertDoc()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        foreach (string marker in new[] { InsertedA, InsertedB })
        {
            PdfPageBuilder page = builder.AddPage(595, 842);
            page.AddText(marker, 12, new PdfPoint(50, 800), font);
        }

        string path = Path.Combine(_tmpDir, "insert-" + Guid.NewGuid().ToString("N") + ".pdf");
        File.WriteAllBytes(path, builder.Build());
        return path;
    }

    private static int PageCount(string path)
    {
        using var doc = PdfPigDocument.Open(path);
        return doc.NumberOfPages;
    }

    private static IReadOnlyList<string> PageMarkers(string path)
    {
        using var doc = PdfPigDocument.Open(path);
        return doc.GetPages().Select(p => Marker(p.Text)).ToList();
    }

    private static string Marker(string text)
    {
        // Source asset pages begin with "Page {N} of 10"; inserted pages begin with "INSERTED A/B".
        var match = Regex.Match(text, @"^(Page \d+ of 10|INSERTED [AB])");
        match.Success.Should().BeTrue("every output page is tagged with a known marker (got: '{0}')", text);
        return match.Value;
    }

    private static byte[] Sha256(string path) => SHA256.HashData(File.ReadAllBytes(path));

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
