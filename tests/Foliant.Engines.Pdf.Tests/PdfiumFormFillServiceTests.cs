using System.Globalization;
using System.Text;
using FluentAssertions;
using Foliant.Engines.Pdf;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Slow round-trip: пишем значения PDFium write-back'ом, читаем обратно managed-кодом
/// (PdfPig) — проверяем, что <c>/V</c>-entry оказалась в PDF и не сломалась структура.
/// </summary>
[Trait("Category", "Slow")]
public sealed class PdfiumFormFillServiceTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly PdfiumFormFillService _fill;
    private readonly PdfPigFormReader _reader;

    public PdfiumFormFillServiceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-form-fill-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
        _fill = new PdfiumFormFillService();
        _reader = new PdfPigFormReader(NullLogger<PdfPigFormReader>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task ApplyAsync_SetsValueOnNamedField()
    {
        // Empty field /T (FullName), no /V — value should be empty pre-apply.
        string source = WriteAcroFormPdf(new[] { "/FT /Tx /T (FullName)" });
        string target = Path.Combine(_tmpDir, "out.pdf");

        await _fill.ApplyAsync(
            source,
            new Dictionary<string, string> { ["FullName"] = "Alice Cooper" },
            target,
            CancellationToken.None);

        var read = await _reader.ReadAsync(target, CancellationToken.None);
        read.Should().ContainKey("FullName")
            .WhoseValue.Should().Be("Alice Cooper");
    }

    [Fact]
    public async Task ApplyAsync_OverwritesExistingValue()
    {
        string source = WriteAcroFormPdf(new[] { "/FT /Tx /T (Status) /V (Draft)" });
        string target = Path.Combine(_tmpDir, "out.pdf");

        await _fill.ApplyAsync(
            source,
            new Dictionary<string, string> { ["Status"] = "Approved" },
            target,
            CancellationToken.None);

        var read = await _reader.ReadAsync(target, CancellationToken.None);
        read["Status"].Should().Be("Approved");
    }

    [Fact]
    public async Task ApplyAsync_LeavesUnmappedFieldsAlone()
    {
        string source = WriteAcroFormPdf(new[]
        {
            "/FT /Tx /T (FirstName) /V (Bob)",
            "/FT /Tx /T (LastName) /V (Marley)",
        });
        string target = Path.Combine(_tmpDir, "out.pdf");

        // Map only FirstName; LastName must stay as in source.
        await _fill.ApplyAsync(
            source,
            new Dictionary<string, string> { ["FirstName"] = "Robert" },
            target,
            CancellationToken.None);

        var read = await _reader.ReadAsync(target, CancellationToken.None);
        read["FirstName"].Should().Be("Robert");
        read["LastName"].Should().Be("Marley");
    }

    [Fact]
    public async Task ApplyAsync_UnknownFieldNames_AreIgnored()
    {
        // Best-effort: an extra key in the values dict that isn't in the PDF must NOT throw.
        string source = WriteAcroFormPdf(new[] { "/FT /Tx /T (FieldA) /V (orig)" });
        string target = Path.Combine(_tmpDir, "out.pdf");

        await _fill.ApplyAsync(
            source,
            new Dictionary<string, string> { ["FieldA"] = "new", ["FieldZ"] = "ghost" },
            target,
            CancellationToken.None);

        var read = await _reader.ReadAsync(target, CancellationToken.None);
        read["FieldA"].Should().Be("new");
        read.Should().NotContainKey("FieldZ");
    }

    [Fact]
    public async Task ApplyAsync_EmptyValueClearsField()
    {
        string source = WriteAcroFormPdf(new[] { "/FT /Tx /T (Note) /V (initial)" });
        string target = Path.Combine(_tmpDir, "out.pdf");

        await _fill.ApplyAsync(
            source,
            new Dictionary<string, string> { ["Note"] = "" },
            target,
            CancellationToken.None);

        var read = await _reader.ReadAsync(target, CancellationToken.None);
        read["Note"].Should().Be("");
    }

    [Fact]
    public async Task ApplyAsync_BlankSourcePath_Throws()
    {
        Func<Task> act = async () => await _fill.ApplyAsync(
            "   ", new Dictionary<string, string>(), Path.Combine(_tmpDir, "out.pdf"), CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ApplyAsync_NullValues_Throws()
    {
        Func<Task> act = async () => await _fill.ApplyAsync(
            Path.Combine(_tmpDir, "x.pdf"), null!, Path.Combine(_tmpDir, "out.pdf"), CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    /// <summary>Same hand-builder as <see cref="PdfPigFormReaderTests"/> — keeps the test
    /// self-contained without making the helper public.</summary>
    private string WriteAcroFormPdf(string[] fieldDictBodies)
    {
        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");

        var offsets = new List<int> { 0 };

        offsets.Add(sb.Length);
        sb.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [");
        for (int i = 0; i < fieldDictBodies.Length; i++)
        {
            if (i > 0) { sb.Append(' '); }
            sb.Append((4 + i).ToString(CultureInfo.InvariantCulture)).Append(" 0 R");
        }
        sb.Append("] >> >>\nendobj\n");

        offsets.Add(sb.Length);
        sb.Append("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        offsets.Add(sb.Length);
        sb.Append("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Annots [");
        for (int i = 0; i < fieldDictBodies.Length; i++)
        {
            if (i > 0) { sb.Append(' '); }
            sb.Append((4 + i).ToString(CultureInfo.InvariantCulture)).Append(" 0 R");
        }
        sb.Append("] >>\nendobj\n");

        for (int i = 0; i < fieldDictBodies.Length; i++)
        {
            offsets.Add(sb.Length);
            int objNum = 4 + i;
            sb.Append(objNum.ToString(CultureInfo.InvariantCulture))
              .Append(" 0 obj\n<< /Type /Annot /Subtype /Widget ")
              .Append(fieldDictBodies[i])
              .Append(" /Rect [100 700 300 720] >>\nendobj\n");
        }

        int xrefOffset = sb.Length;
        sb.Append("xref\n0 ").Append((1 + offsets.Count - 1).ToString(CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("0000000000 65535 f \n");
        for (int i = 1; i < offsets.Count; i++)
        {
            sb.Append(offsets[i].ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        sb.Append("trailer\n<< /Size ").Append((offsets.Count).ToString(CultureInfo.InvariantCulture))
          .Append(" /Root 1 0 R >>\nstartxref\n").Append(xrefOffset.ToString(CultureInfo.InvariantCulture))
          .Append("\n%%EOF\n");

        string path = Path.Combine(_tmpDir, "form-" + Guid.NewGuid().ToString("N") + ".pdf");
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes(sb.ToString()));
        return path;
    }
}
