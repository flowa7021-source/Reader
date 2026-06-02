using System.Globalization;
using System.Runtime.InteropServices;
using FluentAssertions;
using Foliant.Domain;
using PDFiumCore;
using UglyToad.PdfPig.Tokens;
using Xunit;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Round-trip integration tests for Line/Arrow/Polygon (Q-F17 11/11). PDFium 146.x не умеет
/// embedд'ить /L/Vertices/LE setter'ами, поэтому <see cref="AnnotatedPdfExportService"/> вызывает
/// cos-level fallback (<see cref="PdfPigAnnotationAppender"/>) после save'а PDFium. Эти тесты
/// проверяют, что output PDF содержит native /Annots объекты с правильными /Subtype /Line|/Polygon
/// и геометрией. Помечены Slow — нужен PDFium runtime для базового save'а.
/// </summary>
[Trait("Category", "Slow")]
public sealed class AnnotatedPdfExportServiceLineArrowPolygonTests : IDisposable
{
    private const int SubtypeHighlight = 9;
    private const int SubtypeText = 1;

    private readonly string _tmpDir;
    private readonly AnnotatedPdfExportService _service = new();

    public AnnotatedPdfExportServiceLineArrowPolygonTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-annot-cos-tests-" + Guid.NewGuid().ToString("N"));
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
    public async Task Export_Line_EmbedsNativeLineAnnotationWithExactCoordinates()
    {
        string source = SourcePath();
        string target = Path.Combine(_tmpDir, "annotated-line.pdf");
        var when = DateTimeOffset.UnixEpoch;

        var annotations = new[]
        {
            Annotation.Line(0, [new(50, 60), new(200, 180)], "#FF0000", when),
        };

        await _service.ExportAsync(source, annotations, target, default);
        File.Exists(target).Should().BeTrue();

        var page0Annots = ReadAnnotationDictionaries(target, pageIndex: 0);
        page0Annots.Should().ContainSingle("ровно одна Line должна осесть в /Annots страницы 0");

        var dict = page0Annots[0];
        ReadName(dict, "Subtype").Should().Be("Line");
        var l = ReadNumberArray(dict, "L");
        l.Should().Equal(50d, 60d, 200d, 180d);
        dict.ContainsKey(NameToken.Create("LE")).Should().BeFalse("plain Line не имеет /LE");
        // /C — RGB в [0..1]
        var c = ReadNumberArray(dict, "C");
        c.Should().HaveCount(3);
        c[0].Should().BeApproximately(1.0, 0.01);
        c[1].Should().BeApproximately(0.0, 0.01);
        c[2].Should().BeApproximately(0.0, 0.01);
    }

    [Fact]
    public async Task Export_Arrow_AddsOpenArrowLineEnding()
    {
        string source = SourcePath();
        string target = Path.Combine(_tmpDir, "annotated-arrow.pdf");
        var when = DateTimeOffset.UnixEpoch;

        var annotations = new[]
        {
            Annotation.Arrow(0, [new(100, 100), new(300, 250)], "#00AA00", when),
        };

        await _service.ExportAsync(source, annotations, target, default);

        var dicts = ReadAnnotationDictionaries(target, pageIndex: 0);
        dicts.Should().ContainSingle();
        var d = dicts[0];
        ReadName(d, "Subtype").Should().Be("Line");
        ReadNumberArray(d, "L").Should().Equal(100d, 100d, 300d, 250d);

        d.TryGet(NameToken.Create("LE"), out ArrayToken? le).Should().BeTrue();
        le.Should().NotBeNull();
        le!.Data.Should().HaveCount(2);
        ((NameToken)le.Data[0]).Data.Should().Be("None", "start конца стрелки — без декорации");
        ((NameToken)le.Data[1]).Data.Should().Be("OpenArrow", "end конца стрелки — open-arrowhead");
    }

    [Fact]
    public async Task Export_Polygon_EmbedsVerticesInOrder()
    {
        string source = SourcePath();
        string target = Path.Combine(_tmpDir, "annotated-polygon.pdf");
        var when = DateTimeOffset.UnixEpoch;

        var verts = new AnnotationPoint[]
        {
            new(50, 50),
            new(200, 50),
            new(200, 150),
            new(125, 200),
            new(50, 150),
        };

        var annotations = new[]
        {
            Annotation.Polygon(0, verts, "#0000FF", when),
        };

        await _service.ExportAsync(source, annotations, target, default);

        var dicts = ReadAnnotationDictionaries(target, pageIndex: 0);
        dicts.Should().ContainSingle();
        var d = dicts[0];
        ReadName(d, "Subtype").Should().Be("Polygon");
        var v = ReadNumberArray(d, "Vertices");
        v.Should().Equal(50d, 50d, 200d, 50d, 200d, 150d, 125d, 200d, 50d, 150d);

        // /Rect = bbox по всем точкам.
        var rect = ReadNumberArray(d, "Rect");
        rect[0].Should().BeApproximately(50, 0.01);
        rect[1].Should().BeApproximately(50, 0.01);
        rect[2].Should().BeApproximately(200, 0.01);
        rect[3].Should().BeApproximately(200, 0.01);
    }

    [Fact]
    public async Task Export_LineAndHighlightOnSamePage_PreservesExistingPdfiumAnnotations()
    {
        // Сценарий регрессии: смешанный набор. PDFium-аннотации должны остаться нетронутыми
        // после инкрементального апдейта, а Line — добавиться как дополнительная.
        string source = SourcePath();
        string target = Path.Combine(_tmpDir, "annotated-mixed.pdf");
        var when = DateTimeOffset.UnixEpoch;

        var annotations = new[]
        {
            Annotation.Highlight(0, new AnnotationRect(50, 100, 200, 20), "#FFFF00", when),
            Annotation.StickyNote(0, new AnnotationRect(300, 300, 20, 20), "Note", "#FF0000", when),
            Annotation.Line(0, [new(50, 400), new(250, 500)], "#0000FF", when),
        };

        await _service.ExportAsync(source, annotations, target, default);

        // PDFium API: подсчёт всех annotations на странице 0; ожидаем 3 = highlight + note + line.
        WithDocument(target, doc =>
        {
            var page = fpdfview.FPDF_LoadPage(doc, 0);
            try
            {
                int count = fpdf_annot.FPDFPageGetAnnotCount(page);
                count.Should().Be(3, "highlight + note (PDFium) + line (cos-fallback)");

                // Verify Line subtype присутствует среди annotations. FPDF_ANNOT_LINE = 4 в fpdf_annot.h.
                bool foundLine = false;
                bool foundHighlight = false;
                bool foundText = false;
                for (int i = 0; i < count; i++)
                {
                    var annot = fpdf_annot.FPDFPageGetAnnot(page, i);
                    try
                    {
                        int subtype = fpdf_annot.FPDFAnnotGetSubtype(annot);
                        if (subtype == 4) { foundLine = true; }
                        if (subtype == SubtypeHighlight) { foundHighlight = true; }
                        if (subtype == SubtypeText) { foundText = true; }
                    }
                    finally
                    {
                        fpdf_annot.FPDFPageCloseAnnot(annot);
                    }
                }

                foundLine.Should().BeTrue("cos-fallback должен embedд'ить Line как /Subtype /Line");
                foundHighlight.Should().BeTrue("PDFium-highlight должен сохраниться");
                foundText.Should().BeTrue("PDFium-sticky-note должен сохраниться");
            }
            finally
            {
                fpdfview.FPDF_ClosePage(page);
            }
        });
    }

    [Fact]
    public async Task Export_LinePolygonArrowOnDifferentPages_EachLandsOnItsPage()
    {
        string source = SourcePath();
        string target = Path.Combine(_tmpDir, "annotated-multipage.pdf");
        var when = DateTimeOffset.UnixEpoch;

        var annotations = new[]
        {
            Annotation.Line(0, [new(10, 10), new(50, 50)], "#FF0000", when),
            Annotation.Arrow(1, [new(20, 20), new(80, 90)], "#00FF00", when),
            Annotation.Polygon(2, [new(30, 30), new(70, 30), new(50, 80)], "#0000FF", when),
        };

        await _service.ExportAsync(source, annotations, target, default);
        File.Exists(target).Should().BeTrue();

        var p0 = ReadAnnotationDictionaries(target, pageIndex: 0);
        var p1 = ReadAnnotationDictionaries(target, pageIndex: 1);
        var p2 = ReadAnnotationDictionaries(target, pageIndex: 2);

        p0.Should().ContainSingle();
        ReadName(p0[0], "Subtype").Should().Be("Line");
        p0[0].ContainsKey(NameToken.Create("LE")).Should().BeFalse();

        p1.Should().ContainSingle();
        ReadName(p1[0], "Subtype").Should().Be("Line");
        p1[0].TryGet(NameToken.Create("LE"), out ArrayToken? le1).Should().BeTrue();
        le1!.Data.Should().HaveCount(2);
        ((NameToken)le1.Data[1]).Data.Should().Be("OpenArrow");

        p2.Should().ContainSingle();
        ReadName(p2[0], "Subtype").Should().Be("Polygon");
    }

    [Fact]
    public async Task Export_LineWithUnicodeAuthorAndSubject_RoundTripsThroughUtf16BeHexString()
    {
        // Cos-writer должен корректно сериализовать русский /T и /Subj как hex-string с BOM,
        // иначе любой не-ASCII в metadata поломает PDF.
        string source = SourcePath();
        string target = Path.Combine(_tmpDir, "annotated-line-cyrillic.pdf");
        var when = DateTimeOffset.UnixEpoch;

        var line = Annotation.Line(0, [new(10, 10), new(50, 50)], "#000000", when) with
        {
            Author = "Алиса",
            Subject = "Заметка по схеме",
        };

        await _service.ExportAsync(source, new[] { line }, target, default);

        var dicts = ReadAnnotationDictionaries(target, pageIndex: 0);
        dicts.Should().ContainSingle();
        var d = dicts[0];

        // /T → hex UTF-16BE с BOM; PdfPig HexToken даст нам bytes, мы декодируем обратно.
        d.TryGet(NameToken.Create("T"), out HexToken? authorHex).Should().BeTrue();
        DecodeUtf16BeHex(authorHex!).Should().Be("Алиса");

        d.TryGet(NameToken.Create("Subj"), out HexToken? subjHex).Should().BeTrue();
        DecodeUtf16BeHex(subjHex!).Should().Be("Заметка по схеме");
    }

    [Fact]
    public async Task Export_LinePolygonArrow_OutputRemainsLoadableByPdfium()
    {
        // Регрессия: cos-level append не должен ломать структуру PDF — PDFium должен прочесть
        // итоговый файл и увидеть тот же page count + новые annotations.
        string source = SourcePath();
        string target = Path.Combine(_tmpDir, "annotated-validity.pdf");
        var when = DateTimeOffset.UnixEpoch;

        var annotations = new[]
        {
            Annotation.Line(0, [new(10, 10), new(50, 50)], "#000", when),
            Annotation.Arrow(1, [new(10, 10), new(50, 50)], "#000", when),
            Annotation.Polygon(2, [new(10, 10), new(20, 10), new(15, 20)], "#000", when),
        };

        await _service.ExportAsync(source, annotations, target, default);

        int expectedPages = SourcePageCount(source);
        WithDocument(target, doc => fpdfview.FPDF_GetPageCount(doc).Should().Be(expectedPages));

        // Проверяем что итоговый PDF тоже открывается PdfPig'ом без падения.
        using var pp = PdfPigDocument.Open(target);
        pp.NumberOfPages.Should().Be(expectedPages);
    }

    // --- helpers ---

    private static string DecodeUtf16BeHex(HexToken hex)
    {
        ReadOnlySpan<byte> bytes = hex.Memory.Span;
        // BOM FE FF — skip first 2 bytes if present.
        int offset = bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF ? 2 : 0;
        return System.Text.Encoding.BigEndianUnicode.GetString(bytes[offset..]);
    }

    private static IReadOnlyList<DictionaryToken> ReadAnnotationDictionaries(string pdfPath, int pageIndex)
    {
        // Читаем /Annots напрямую из page-словаря через PdfPig — наш cos-writer выдаёт inline-refs
        // на наши новые объекты + сохраняет существующие entries.
        var result = new List<DictionaryToken>();
        using var doc = PdfPigDocument.Open(pdfPath);
        var page = doc.GetPage(pageIndex + 1);
        if (!page.Dictionary.TryGet(NameToken.Annots, out IToken? annotsRaw))
        {
            return result;
        }

        ArrayToken annotsArr = annotsRaw switch
        {
            ArrayToken arr => arr,
            IndirectReferenceToken iref =>
                doc.Structure.GetObject(iref.Data) is ObjectToken { Data: ArrayToken r } ? r : new ArrayToken([]),
            _ => new ArrayToken([]),
        };

        foreach (var item in annotsArr.Data)
        {
            switch (item)
            {
                case DictionaryToken inline:
                    if (IsOurSubtype(inline))
                    {
                        result.Add(inline);
                    }
                    break;
                case IndirectReferenceToken iref:
                    if (doc.Structure.GetObject(iref.Data) is ObjectToken { Data: DictionaryToken resolved } &&
                        IsOurSubtype(resolved))
                    {
                        result.Add(resolved);
                    }
                    break;
            }
        }

        return result;
    }

    private static bool IsOurSubtype(DictionaryToken d)
    {
        if (!d.TryGet(NameToken.Subtype, out NameToken? st) || st is null)
        {
            return false;
        }

        return st.Data is "Line" or "Polygon";
    }

    private static string ReadName(DictionaryToken d, string key)
    {
        d.TryGet(NameToken.Create(key), out NameToken? n).Should().BeTrue();
        n.Should().NotBeNull();
        return n!.Data;
    }

    private static double[] ReadNumberArray(DictionaryToken d, string key)
    {
        d.TryGet(NameToken.Create(key), out ArrayToken? arr).Should().BeTrue();
        arr.Should().NotBeNull();
        var result = new double[arr!.Data.Count];
        for (int i = 0; i < arr.Data.Count; i++)
        {
            result[i] = ((NumericToken)arr.Data[i]).Data;
        }

        return result;
    }

    private static int SourcePageCount(string path)
    {
        fpdfview.FPDF_InitLibrary();
        byte[] bytes = File.ReadAllBytes(path);
        GCHandle pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var doc = fpdfview.FPDF_LoadMemDocument64(pin.AddrOfPinnedObject(), (ulong)bytes.LongLength, null);
            try
            {
                return fpdfview.FPDF_GetPageCount(doc);
            }
            finally
            {
                fpdfview.FPDF_CloseDocument(doc);
            }
        }
        finally
        {
            pin.Free();
        }
    }

    private static void WithDocument(string path, Action<FpdfDocumentT> body)
    {
        fpdfview.FPDF_InitLibrary();
        byte[] bytes = File.ReadAllBytes(path);
        GCHandle pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var doc = fpdfview.FPDF_LoadMemDocument64(pin.AddrOfPinnedObject(), (ulong)bytes.LongLength, null);
            doc.Should().NotBeNull("итоговый PDF должен быть валидным с точки зрения PDFium");
            try
            {
                body(doc);
            }
            finally
            {
                fpdfview.FPDF_CloseDocument(doc);
            }
        }
        finally
        {
            pin.Free();
        }
    }

    private static string SourcePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Foliant.sln")))
            {
                return Path.Combine(dir.FullName, "tests", "assets", "pdf-text-ru-10p.pdf");
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root (Foliant.sln).");
    }
}
