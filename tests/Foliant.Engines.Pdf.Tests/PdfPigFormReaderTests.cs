using System.Globalization;
using System.Text;
using FluentAssertions;
using Foliant.Engines.Pdf;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Engines.Pdf.Tests;

public sealed class PdfPigFormReaderTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly PdfPigFormReader _reader;

    public PdfPigFormReaderTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-form-reader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
        _reader = new PdfPigFormReader(NullLogger<PdfPigFormReader>.Instance);
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
    public async Task ReadAsync_PdfWithoutForm_ReturnsEmpty()
    {
        // Маленький PDF без AcroForm — синтезируем через PdfPig writer.
        string path = WritePlainPdf();

        var result = await _reader.ReadAsync(path, default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_CorruptOrMissingFile_ReturnsEmpty_NotThrow()
    {
        string missing = Path.Combine(_tmpDir, "does-not-exist.pdf");

        var result = await _reader.ReadAsync(missing, default);

        // Контракт: best-effort; читатель не должен ронять caller'а на битом источнике.
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_AcroForm_WithTextField_ReadsValue()
    {
        string path = WriteAcroFormPdf(new[]
        {
            ("/FT /Tx /T (FullName) /V (Alice Cooper)", null as string),
        });

        var result = await _reader.ReadAsync(path, default);

        result.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new KeyValuePair<string, string>("FullName", "Alice Cooper"));
    }

    [Fact]
    public async Task ReadAsync_AcroForm_MultipleTextFields_ReadsAll()
    {
        string path = WriteAcroFormPdf(new[]
        {
            ("/FT /Tx /T (FirstName) /V (Bob)", null as string),
            ("/FT /Tx /T (LastName) /V (Marley)", null as string),
            ("/FT /Tx /T (City) /V (Kingston)", null as string),
        });

        var result = await _reader.ReadAsync(path, default);

        result.Should().BeEquivalentTo(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FirstName"] = "Bob",
            ["LastName"] = "Marley",
            ["City"] = "Kingston",
        });
    }

    [Fact]
    public async Task ReadAsync_AcroForm_TextFieldWithoutValue_ReadsEmpty()
    {
        string path = WriteAcroFormPdf(new[]
        {
            ("/FT /Tx /T (Empty)", null as string),
        });

        var result = await _reader.ReadAsync(path, default);

        result.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new KeyValuePair<string, string>("Empty", ""));
    }

    /// <summary>Минимальный «голый» PDF без формы — собираем через PdfPig'овский builder.</summary>
    private string WritePlainPdf()
    {
        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        var page = builder.AddPage(width: 200, height: 200);
        page.AddText("Plain page", 12, new UglyToad.PdfPig.Core.PdfPoint(20, 100), font);

        string path = Path.Combine(_tmpDir, "plain-" + Guid.NewGuid().ToString("N") + ".pdf");
        File.WriteAllBytes(path, builder.Build());
        return path;
    }

    /// <summary>Hand-build минимального AcroForm-PDF с произвольным набором widget-полей.
    /// Каждый element tuple = (inner-dict-body, ignored). PdfPig толерантен к минимализму:
    /// нам нужны только корректный xref и /AcroForm-словарь.</summary>
    private string WriteAcroFormPdf((string FieldDictBody, string? _)[] fields)
    {
        // Соберём список object'ов: 1=Catalog, 2=Pages, 3=Page, 4..N=Widget-полей.
        var sb = new StringBuilder();
        // Header — \n обязателен; добавляем binary-marker comment (рекомендация PDF spec).
        sb.Append("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");

        var offsets = new List<int> { 0 };  // object 0 — placeholder

        // 1: Catalog
        offsets.Add(sb.Length);
        sb.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [");
        for (int i = 0; i < fields.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            sb.Append((4 + i).ToString(CultureInfo.InvariantCulture)).Append(" 0 R");
        }
        sb.Append("] >> >>\nendobj\n");

        // 2: Pages
        offsets.Add(sb.Length);
        sb.Append("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        // 3: Page (referencing widgets in /Annots)
        offsets.Add(sb.Length);
        sb.Append("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Annots [");
        for (int i = 0; i < fields.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            sb.Append((4 + i).ToString(CultureInfo.InvariantCulture)).Append(" 0 R");
        }
        sb.Append("] >>\nendobj\n");

        // 4..N: Widget annot/field objects
        for (int i = 0; i < fields.Length; i++)
        {
            offsets.Add(sb.Length);
            int objNum = 4 + i;
            sb.Append(objNum.ToString(CultureInfo.InvariantCulture))
              .Append(" 0 obj\n<< /Type /Annot /Subtype /Widget ")
              .Append(fields[i].FieldDictBody)
              .Append(" /Rect [100 700 300 720] >>\nendobj\n");
        }

        // xref
        int xrefOffset = sb.Length;
        sb.Append("xref\n0 ").Append((1 + offsets.Count - 1).ToString(CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("0000000000 65535 f \n");
        for (int i = 1; i < offsets.Count; i++)
        {
            sb.Append(offsets[i].ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        // trailer
        sb.Append("trailer\n<< /Size ").Append((offsets.Count).ToString(CultureInfo.InvariantCulture))
          .Append(" /Root 1 0 R >>\nstartxref\n").Append(xrefOffset.ToString(CultureInfo.InvariantCulture))
          .Append("\n%%EOF\n");

        string path = Path.Combine(_tmpDir, "form-" + Guid.NewGuid().ToString("N") + ".pdf");
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes(sb.ToString()));
        return path;
    }
}
