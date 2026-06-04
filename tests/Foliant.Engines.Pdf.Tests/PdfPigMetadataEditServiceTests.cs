using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Foliant.Domain;
using Foliant.Engines.Pdf;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Exercises <see cref="PdfPigMetadataEditService"/> — classic /Info editing via PdfPig re-serialization.
/// Pure-managed (PdfPig) — green under the Linux unit filter, no PDFium needed.
/// Most sources are built in-test with known /Info; the committed 10-page asset checks page fidelity.
/// </summary>
public sealed class PdfPigMetadataEditServiceTests : IDisposable
{
    private readonly string _tmpDir;

    public PdfPigMetadataEditServiceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-meta-" + Guid.NewGuid().ToString("N"));
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

    // (a) Setting a single field writes it through to the new /Info.
    [Fact]
    public async Task EditAsync_SetTitle_WritesNewTitle()
    {
        string src = WritePdf(pages: 2, title: "Old Title");
        string dst = Path.Combine(_tmpDir, "title.pdf");

        await new PdfPigMetadataEditService().EditAsync(src, dst, new PdfMetadataSpec(Title: "New Title"), default);

        Info(dst).Title.Should().Be("New Title");
    }

    // (b) A null spec field preserves the source value (set only Author; Title stays).
    [Fact]
    public async Task EditAsync_NullField_PreservesExistingValue()
    {
        string src = WritePdf(pages: 1, title: "Keep Me", author: "Old Author");
        string dst = Path.Combine(_tmpDir, "preserve.pdf");

        await new PdfPigMetadataEditService().EditAsync(src, dst, new PdfMetadataSpec(Author: "New Author"), default);

        var info = Info(dst);
        info.Title.Should().Be("Keep Me", "null Title means 'do not change'");
        info.Author.Should().Be("New Author");
    }

    // (c) An empty string clears the field (distinct from null = keep).
    [Fact]
    public async Task EditAsync_EmptyString_ClearsField()
    {
        string src = WritePdf(pages: 1, title: "Has Title");
        string dst = Path.Combine(_tmpDir, "clear.pdf");

        await new PdfPigMetadataEditService().EditAsync(src, dst, new PdfMetadataSpec(Title: ""), default);

        // PdfPig writes an empty /Info entry; PdfDocument reads it back as empty string (not null).
        Info(dst).Title.Should().BeEmpty();
    }

    // (d) All six fields round-trip simultaneously.
    [Fact]
    public async Task EditAsync_AllSixFields_RoundTrip()
    {
        string src = WritePdf(pages: 1);
        string dst = Path.Combine(_tmpDir, "all.pdf");
        var spec = new PdfMetadataSpec(
            Title: "T", Author: "A", Subject: "S", Keywords: "K1,K2", Creator: "C", Producer: "P");

        await new PdfPigMetadataEditService().EditAsync(src, dst, spec, default);

        var info = Info(dst);
        info.Title.Should().Be("T");
        info.Author.Should().Be("A");
        info.Subject.Should().Be("S");
        info.Keywords.Should().Be("K1,K2");
        info.Creator.Should().Be("C");
        info.Producer.Should().Be("P");
    }

    // (e) Page count is preserved (10 -> 10): the document is not corrupted by the edit.
    [Fact]
    public async Task EditAsync_PreservesPageCount()
    {
        string dst = Path.Combine(_tmpDir, "pages.pdf");

        await new PdfPigMetadataEditService().EditAsync(Asset, dst, new PdfMetadataSpec(Title: "Stamped"), default);

        PageCount(dst).Should().Be(10);
        Info(dst).Title.Should().Be("Stamped");
    }

    // (f) The original file is never mutated (byte-identical before/after via SHA-256).
    [Fact]
    public async Task EditAsync_DoesNotMutateSource()
    {
        string src = WritePdf(pages: 3, title: "Immutable");
        string dst = Path.Combine(_tmpDir, "out.pdf");
        string before = Sha256(src);

        await new PdfPigMetadataEditService().EditAsync(src, dst, new PdfMetadataSpec(Title: "Different"), default);

        Sha256(src).Should().Be(before, "the source must be left byte-identical (atomic temp + Move on target only)");
    }

    // (g) Error contract: blank paths throw ArgumentException, null spec throws ArgumentNullException.
    [Theory]
    [InlineData("", "out.pdf")]
    [InlineData("   ", "out.pdf")]
    [InlineData("src.pdf", "")]
    [InlineData("src.pdf", "   ")]
    public async Task EditAsync_BlankPath_ThrowsArgumentException(string source, string target)
    {
        var act = () => new PdfPigMetadataEditService().EditAsync(source, target, new PdfMetadataSpec(Title: "x"), default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task EditAsync_NullSpec_ThrowsArgumentNullException()
    {
        string src = WritePdf(pages: 1);

        var act = () => new PdfPigMetadataEditService().EditAsync(src, Path.Combine(_tmpDir, "x.pdf"), null!, default);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // (h) Fidelity: annotations survive the metadata-edit round-trip.
    // Result (PdfPig 0.1.10): Text + Link annotations and their content ARE preserved — no known
    // limitation here. If a future PdfPig regressed this, flip the assertion to document the loss.
    [Fact]
    public async Task EditAsync_PreservesAnnotations()
    {
        string src = Path.Combine(_tmpDir, "annot-src.pdf");
        File.WriteAllBytes(src, AnnotatedPdfBytes());
        string dst = Path.Combine(_tmpDir, "annot-out.pdf");
        AnnotationSummary(src).Should().Be("Text:A note|Link:", "the crafted source has a Text + Link annotation");

        await new PdfPigMetadataEditService().EditAsync(src, dst, new PdfMetadataSpec(Title: "Stamped"), default);

        AnnotationSummary(dst).Should().Be("Text:A note|Link:", "PdfMerger re-serialization keeps annotations intact");
        Info(dst).Title.Should().Be("Stamped");
    }

    // No .tmp files linger after an atomic write.
    [Fact]
    public async Task EditAsync_WritesAtomically_NoTmpFilesLinger()
    {
        string src = WritePdf(pages: 1);

        await new PdfPigMetadataEditService().EditAsync(src, Path.Combine(_tmpDir, "atomic.pdf"), new PdfMetadataSpec(Title: "x"), default);

        Directory.EnumerateFiles(_tmpDir, ".*.tmp").Should().BeEmpty();
        Directory.EnumerateFiles(_tmpDir, "*.tmp").Should().BeEmpty();
    }

    private string WritePdf(int pages, string? title = null, string? author = null)
    {
        var builder = new PdfDocumentBuilder();
        if (title is not null)
        {
            builder.DocumentInformation.Title = title;
        }

        if (author is not null)
        {
            builder.DocumentInformation.Author = author;
        }

        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (int i = 0; i < pages; i++)
        {
            var page = builder.AddPage(width: 200, height: 200);
            page.AddText($"Page {i + 1}", 12, new UglyToad.PdfPig.Core.PdfPoint(20, 100), font);
        }

        string path = Path.Combine(_tmpDir, "src-" + Guid.NewGuid().ToString("N") + ".pdf");
        File.WriteAllBytes(path, builder.Build());
        return path;
    }

    private static string Asset => Path.Combine(ResolveAssetsDir(), "pdf-text-en-10p.pdf");

    private static UglyToad.PdfPig.Content.DocumentInformation Info(string path)
    {
        using var doc = PdfPigDocument.Open(path);
        return doc.Information;
    }

    private static int PageCount(string path)
    {
        using var doc = PdfPigDocument.Open(path);
        return doc.NumberOfPages;
    }

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string AnnotationSummary(string path)
    {
        using var doc = PdfPigDocument.Open(path);
        // "Type:Content" per annotation, joined by '|' — proves both presence and content fidelity.
        var parts = doc.GetPages()
            .SelectMany(p => p.GetAnnotations())
            .Select(a => $"{a.Type}:{a.Content}");
        return string.Join("|", parts);
    }

    /// <summary>
    /// Hand-crafted 1-page PDF carrying a Text (sticky-note) and a Link annotation. PdfDocumentBuilder
    /// cannot emit annotations, so we author the bytes directly to test fidelity through the edit.
    /// </summary>
    private static byte[] AnnotatedPdfBytes()
    {
        var sb = new StringBuilder();
        var offsets = new List<int>();
        void Obj(string body)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(sb.ToString()));
            sb.Append(body);
        }

        sb.Append("%PDF-1.5\n");
        Obj("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        Obj("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        Obj("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << /Font << /F1 6 0 R >> >> /Contents 4 0 R /Annots [5 0 R 7 0 R] >>\nendobj\n");
        const string content = "BT /F1 12 Tf 20 100 Td (Hello) Tj ET";
        Obj($"4 0 obj\n<< /Length {content.Length} >>\nstream\n{content}\nendstream\nendobj\n");
        Obj("5 0 obj\n<< /Type /Annot /Subtype /Text /Rect [50 50 70 70] /Contents (A note) >>\nendobj\n");
        Obj("6 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");
        Obj("7 0 obj\n<< /Type /Annot /Subtype /Link /Rect [10 10 30 30] /Border [0 0 1] /A << /Type /Action /S /URI /URI (https://example.com) >> >>\nendobj\n");

        int xref = Encoding.ASCII.GetByteCount(sb.ToString());
        sb.Append("xref\n0 8\n0000000000 65535 f \n");
        foreach (int o in offsets)
        {
            sb.Append(o.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        sb.Append("trailer\n<< /Size 8 /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

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
