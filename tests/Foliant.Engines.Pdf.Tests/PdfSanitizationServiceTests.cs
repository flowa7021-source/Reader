using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using UglyToad.PdfPig.Tokens;
using Xunit;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Integration tests for <see cref="PdfPigSanitizationService"/>: scan + remove of document-level
/// JavaScript / actions (ISO 32000-1 §12.6 / §12.7). The shipped 10-page asset carries no scripts, so
/// the JS-bearing fixtures are hand-built here (header, objects, xref, trailer with <c>/Root</c>) and
/// validated by opening them with PdfPig before asserting. Pure managed PdfPig — no native runtime — so
/// no Slow trait (mirrors <see cref="PdfAttachmentServiceTests"/>).
/// </summary>
public sealed class PdfSanitizationServiceTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly PdfPigSanitizationService _service = new(NullLogger<PdfPigSanitizationService>.Instance);

    public PdfSanitizationServiceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-sanitize-" + Guid.NewGuid().ToString("N"));
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
    public void Fixture_OpensInPdfPig_WithCatalogScripts()
    {
        // Guards the hand-built fixture: it must be a valid PDF whose catalog actually carries the
        // /OpenAction, /Names → /JavaScript and /AA we rely on in the scan/remove tests below.
        byte[] bytes = BuildScriptedPdf();
        using var doc = PdfPigDocument.Open(bytes);
        var catalog = doc.Structure.Catalog.CatalogDictionary;

        doc.NumberOfPages.Should().Be(1);
        catalog.ContainsKey(NameToken.Create("OpenAction")).Should().BeTrue();
        catalog.ContainsKey(NameToken.Create("AA")).Should().BeTrue();
        catalog.ContainsKey(NameToken.Create("Names")).Should().BeTrue();
    }

    [Fact]
    public async Task Scan_ScriptedFixture_ReportsAllThreeKinds()
    {
        string path = WriteScriptedFixture();

        var report = await _service.ScanAsync(path, default);

        report.HasJavaScriptOpenAction.Should().BeTrue();
        report.DocumentJavaScriptNames.Should().Contain("s1");
        report.HasDocumentAdditionalActions.Should().BeTrue();
        report.HasAnyJavaScriptOrActions.Should().BeTrue();
    }

    [Fact]
    public async Task Scan_CleanAsset_ReportsNothing()
    {
        var report = await _service.ScanAsync(Asset, default);

        report.DocumentJavaScriptNames.Should().BeEmpty();
        report.HasJavaScriptOpenAction.Should().BeFalse();
        report.HasDocumentAdditionalActions.Should().BeFalse();
        report.HasAnyJavaScriptOrActions.Should().BeFalse();
    }

    [Fact]
    public async Task Remove_ScriptedFixture_StripsScriptsButKeepsValidPdf()
    {
        string path = WriteScriptedFixture();
        string target = TargetPath();

        bool removed = await _service.RemoveJavaScriptAndActionsAsync(path, target, default);

        removed.Should().BeTrue();

        var rescan = await _service.ScanAsync(target, default);
        rescan.HasAnyJavaScriptOrActions.Should().BeFalse("all catalog-level scripts/actions must be gone");
        rescan.DocumentJavaScriptNames.Should().BeEmpty();
        rescan.HasJavaScriptOpenAction.Should().BeFalse();
        rescan.HasDocumentAdditionalActions.Should().BeFalse();

        PageCount(target).Should().Be(1, "removing scripts must not corrupt the document");
    }

    [Fact]
    public async Task Remove_ScriptedFixture_PreservesOtherNamesSubtree()
    {
        // The fixture's /Names also holds a /Dests sub-tree that must survive sanitization.
        string path = WriteScriptedFixture();
        string target = TargetPath();

        await _service.RemoveJavaScriptAndActionsAsync(path, target, default);

        using var doc = PdfPigDocument.Open(File.ReadAllBytes(target));
        var names = ResolveCatalogDict(doc, "Names");
        names.Should().NotBeNull("the /Names dictionary must be kept because /Dests remains");
        names!.ContainsKey(NameToken.Create("Dests")).Should().BeTrue("preserved /Dests sub-tree must survive");
        names.ContainsKey(NameToken.Create("JavaScript")).Should().BeFalse("the JS sub-tree must be dropped");
    }

    [Fact]
    public async Task Remove_BenignGoToOpenAction_IsPreserved()
    {
        // /OpenAction here is a destination array ([3 0 R /Fit]) — a benign GoTo, not a script.
        string path = Path.Combine(_tmpDir, "goto.pdf");
        File.WriteAllBytes(path, BuildGoToOpenActionPdf());
        (await _service.ScanAsync(path, default)).HasJavaScriptOpenAction.Should().BeFalse();

        string target = TargetPath();
        bool removed = await _service.RemoveJavaScriptAndActionsAsync(path, target, default);

        removed.Should().BeFalse("a GoTo open-action is not a script, so nothing should be removed");

        using var doc = PdfPigDocument.Open(File.ReadAllBytes(target));
        doc.Structure.Catalog.CatalogDictionary
            .ContainsKey(NameToken.Create("OpenAction")).Should().BeTrue("the benign GoTo open-action must survive");
    }

    [Fact]
    public async Task Remove_CleanAsset_ReturnsFalse_AndKeepsValidPdf()
    {
        string target = TargetPath();

        bool removed = await _service.RemoveJavaScriptAndActionsAsync(Asset, target, default);

        removed.Should().BeFalse("the clean asset has no scripts to remove");
        PageCount(target).Should().Be(10);
        (await _service.ScanAsync(target, default)).HasAnyJavaScriptOrActions.Should().BeFalse();
    }

    [Fact]
    public async Task Remove_DoesNotMutateSource()
    {
        string path = WriteScriptedFixture();
        string before = Sha256(path);

        await _service.RemoveJavaScriptAndActionsAsync(path, TargetPath(), default);

        Sha256(path).Should().Be(before, "remove must not touch the source file");
    }

    [Fact]
    public async Task Scan_PdfWithJavaScriptOpenActionOnly_DetectsOpenActionOnly()
    {
        string path = Path.Combine(_tmpDir, "open-only.pdf");
        File.WriteAllBytes(path, BuildJavaScriptOpenActionOnlyPdf());

        var report = await _service.ScanAsync(path, default);

        report.HasJavaScriptOpenAction.Should().BeTrue();
        report.DocumentJavaScriptNames.Should().BeEmpty();
        report.HasDocumentAdditionalActions.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Scan_BlankPath_Throws(string blank)
    {
        var act = () => _service.ScanAsync(blank, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Remove_BlankSource_Throws(string blank)
    {
        var act = () => _service.RemoveJavaScriptAndActionsAsync(blank, TargetPath(), default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Remove_BlankTarget_Throws(string blank)
    {
        var act = () => _service.RemoveJavaScriptAndActionsAsync(Asset, blank, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private string WriteScriptedFixture()
    {
        string path = Path.Combine(_tmpDir, "scripted-" + Guid.NewGuid().ToString("N") + ".pdf");
        File.WriteAllBytes(path, BuildScriptedPdf());
        return path;
    }

    /// <summary>
    /// Hand-builds a valid one-page PDF whose catalog carries every kind of document-level script /
    /// action we sanitize: a JavaScript <c>/OpenAction</c>, a <c>/Names → /JavaScript</c> name-tree
    /// (with a preserved <c>/Dests</c> sibling), and a <c>/AA</c> additional-actions dictionary.
    /// </summary>
    private static byte[] BuildScriptedPdf()
    {
        // Note: literal PDF strings escape '(' and ')' with backslash, e.g. (app.alert\(1\);).
        const string catalog =
            "<< /Type /Catalog /Pages 2 0 R " +
            "/OpenAction << /S /JavaScript /JS (app.alert\\(1\\);) >> " +
            "/Names << " +
            "/JavaScript << /Names [(s1) << /S /JavaScript /JS (x=1;) >>] >> " +
            "/Dests << /Names [(d1) [3 0 R /Fit]] >> " +
            ">> " +
            "/AA << /WillClose << /S /JavaScript /JS (y=2;) >> >> >>";

        return BuildOnePagePdf(catalog);
    }

    /// <summary>Hand-builds a one-page PDF whose <c>/OpenAction</c> is a benign GoTo destination
    /// array (no script) and which carries no other scripts/actions.</summary>
    private static byte[] BuildGoToOpenActionPdf() =>
        BuildOnePagePdf("<< /Type /Catalog /Pages 2 0 R /OpenAction [3 0 R /Fit] >>");

    /// <summary>Hand-builds a one-page PDF whose only script is a JavaScript <c>/OpenAction</c>.</summary>
    private static byte[] BuildJavaScriptOpenActionOnlyPdf() =>
        BuildOnePagePdf("<< /Type /Catalog /Pages 2 0 R /OpenAction << /S /JavaScript /JS (z=3;) >> >>");

    private static byte[] BuildOnePagePdf(string catalogBody)
    {
        using var ms = new MemoryStream();
        var offsets = new long[4];

        WriteAscii(ms, "%PDF-1.7\n%âãÏÓ\n");
        WriteObject(ms, offsets, 1, catalogBody);
        WriteObject(ms, offsets, 2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        WriteObject(ms, offsets, 3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>");

        long xref = ms.Position;
        var sb = new StringBuilder("xref\n0 4\n0000000000 65535 f \n");
        for (int i = 1; i <= 3; i++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{offsets[i]:D10} 00000 n \n");
        }

        sb.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        WriteAscii(ms, sb.ToString());
        return ms.ToArray();
    }

    private static void WriteObject(MemoryStream ms, long[] offsets, int number, string body)
    {
        offsets[number] = ms.Position;
        WriteAscii(ms, string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n{body}\nendobj\n"));
    }

    private static void WriteAscii(MemoryStream ms, string text)
    {
        byte[] bytes = Encoding.Latin1.GetBytes(text);
        ms.Write(bytes, 0, bytes.Length);
    }

    private static DictionaryToken? ResolveCatalogDict(PdfPigDocument doc, string key)
    {
        var catalog = doc.Structure.Catalog.CatalogDictionary;
        if (!catalog.TryGet(NameToken.Create(key), out IToken? raw) || raw is null)
        {
            return null;
        }

        return raw switch
        {
            DictionaryToken inline => inline,
            IndirectReferenceToken iref
                when doc.Structure.GetObject(iref.Data) is ObjectToken { Data: DictionaryToken resolved }
                => resolved,
            _ => null,
        };
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
