using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FluentAssertions;
using Foliant.Domain;
using PDFiumCore;
using Xunit;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Round-trip integration tests for <see cref="AnnotatedPdfExportService"/>. They embed real PDF
/// annotations via PDFium, then re-open the exported file with PDFium and read the annotations back.
/// Marked Slow because they need the native PDFium runtime (<c>libpdfium.so</c>).
/// </summary>
[Trait("Category", "Slow")]
public sealed class AnnotatedPdfExportServiceTests : IDisposable
{
    // PDFium FPDF_ANNOTATION_SUBTYPE values (fpdf_annot.h).
    private const int SubtypeText = 1;
    private const int SubtypeSquare = 5;
    private const int SubtypeCircle = 6;
    private const int SubtypeHighlight = 9;
    private const int SubtypeUnderline = 10;
    private const int SubtypeStrikeout = 12;
    private const int SubtypeStamp = 13;
    private const int SubtypeInk = 15;

    private const string NoteText = "Привет — заметка";

    private readonly string _tmpDir;
    private readonly AnnotatedPdfExportService _service = new();

    public AnnotatedPdfExportServiceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-annot-export-tests-" + Guid.NewGuid().ToString("N"));
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
    public async Task Export_EmbedsHighlightNoteAndInk_RoundTripsViaPdfium()
    {
        string source = SourcePath();
        string target = Path.Combine(_tmpDir, "annotated.pdf");
        var when = DateTimeOffset.UnixEpoch;

        var annotations = new[]
        {
            Annotation.Highlight(0, new AnnotationRect(100, 200, 200, 20), "#FFFF00", when),
            Annotation.StickyNote(1, new AnnotationRect(50, 500, 20, 20), NoteText, "#FF0000", when),
            Annotation.Freehand(
                2,
                [new AnnotationPoint(100, 100), new AnnotationPoint(150, 180), new AnnotationPoint(200, 120)],
                "#0000FF",
                when),
        };

        await _service.ExportAsync(source, annotations, target, default);

        File.Exists(target).Should().BeTrue();

        WithDocument(target, doc =>
        {
            fpdfview.FPDF_GetPageCount(doc).Should().Be(SourcePageCount(source));

            var byPage = ReadAnnotations(doc);

            int total = byPage.Values.Sum(p => p.Count);
            total.Should().Be(3, "exactly one annotation was embedded per page");

            // Highlight on page 0.
            var hi = byPage[0].Should().ContainSingle().Subject;
            hi.Subtype.Should().Be(SubtypeHighlight);
            AssertRect(hi.Rect, left: 100, bottom: 200, right: 300, top: 220);
            (hi.ColorR, hi.ColorG, hi.ColorB).Should().Be(((uint)255, (uint)255, (uint)0));

            // Sticky note on page 1, cyrillic /Contents survives the UTF-16 round trip.
            var note = byPage[1].Should().ContainSingle().Subject;
            note.Subtype.Should().Be(SubtypeText);
            note.Contents.Should().Be(NoteText);

            // Freehand ink on page 2; rect is the ink bounding box from the mapper.
            var ink = byPage[2].Should().ContainSingle().Subject;
            ink.Subtype.Should().Be(SubtypeInk);
            AssertRect(ink.Rect, left: 100, bottom: 100, right: 200, top: 180);
        });
    }

    private static void AssertRect(RectF actual, float left, float bottom, float right, float top)
    {
        const float Tol = 0.5f;
        actual.Left.Should().BeApproximately(left, Tol);
        actual.Bottom.Should().BeApproximately(bottom, Tol);
        actual.Right.Should().BeApproximately(right, Tol);
        actual.Top.Should().BeApproximately(top, Tol);
    }

    [Fact]
    public async Task Export_EmbedsUnderlineAndStrikethrough_RoundTripsViaPdfium()
    {
        string source = SourcePath();
        string target = Path.Combine(_tmpDir, "annotated-textmarkup.pdf");
        var when = DateTimeOffset.UnixEpoch;
        var bounds = new AnnotationRect(50, 300, 200, 12);

        var annotations = new[]
        {
            Annotation.Underline(0, bounds, "#0000FF", when),
            Annotation.Strikethrough(1, bounds, "#FF0000", when),
        };

        await _service.ExportAsync(source, annotations, target, default);

        File.Exists(target).Should().BeTrue();

        WithDocument(target, doc =>
        {
            var byPage = ReadAnnotations(doc);

            var u = byPage[0].Should().ContainSingle().Subject;
            u.Subtype.Should().Be(SubtypeUnderline);
            AssertRect(u.Rect, left: 50, bottom: 300, right: 250, top: 312);
            (u.ColorR, u.ColorG, u.ColorB).Should().Be(((uint)0, (uint)0, (uint)255));

            var s = byPage[1].Should().ContainSingle().Subject;
            s.Subtype.Should().Be(SubtypeStrikeout);
            (s.ColorR, s.ColorG, s.ColorB).Should().Be(((uint)255, (uint)0, (uint)0));
        });
    }

    [Fact]
    public async Task Export_EmbedsRectangleAndEllipse_RoundTripsViaPdfium()
    {
        string source = SourcePath();
        string target = Path.Combine(_tmpDir, "annotated-shapes.pdf");
        var when = DateTimeOffset.UnixEpoch;
        var bounds = new AnnotationRect(50, 200, 100, 60);

        var annotations = new[]
        {
            Annotation.Rectangle(0, bounds, "#00FF00", when),
            Annotation.Ellipse(1, bounds, "#FF00FF", when),
        };

        await _service.ExportAsync(source, annotations, target, default);

        File.Exists(target).Should().BeTrue();

        WithDocument(target, doc =>
        {
            var byPage = ReadAnnotations(doc);

            var sq = byPage[0].Should().ContainSingle().Subject;
            sq.Subtype.Should().Be(SubtypeSquare);
            (sq.ColorR, sq.ColorG, sq.ColorB).Should().Be(((uint)0, (uint)255, (uint)0));

            var ci = byPage[1].Should().ContainSingle().Subject;
            ci.Subtype.Should().Be(SubtypeCircle);
            (ci.ColorR, ci.ColorG, ci.ColorB).Should().Be(((uint)255, (uint)0, (uint)255));
        });
    }

    [Fact]
    public async Task Export_EmbedsStamp_RoundTripsViaPdfium()
    {
        string source = SourcePath();
        string target = Path.Combine(_tmpDir, "annotated-stamp.pdf");
        var when = DateTimeOffset.UnixEpoch;

        var annotations = new[]
        {
            Annotation.Stamp(0, new AnnotationRect(100, 100, 200, 60), "APPROVED", "#00AA00", when),
        };

        await _service.ExportAsync(source, annotations, target, default);

        File.Exists(target).Should().BeTrue();

        WithDocument(target, doc =>
        {
            var stamp = ReadAnnotations(doc)[0].Should().ContainSingle().Subject;
            stamp.Subtype.Should().Be(SubtypeStamp);
            // Label survives via /Contents; color lives in the appearance-stream path/text objects,
            // not in /C — PDFium's FPDFAnnotGetColor returns nothing for Stamp, which is expected.
            stamp.Contents.Should().Be("APPROVED");
            AssertRect(stamp.Rect, left: 100, bottom: 100, right: 300, top: 160);
        });
    }

    [Fact]
    public async Task Export_EmbedsImageStamp_AttachesImageObjectViaPdfium()
    {
        string source = SourcePath();
        string imagePath = MakeSampleImage(64, 32);
        string target = Path.Combine(_tmpDir, "annotated-image-stamp.pdf");
        var when = DateTimeOffset.UnixEpoch;

        var annotations = new[]
        {
            Annotation.ImageStamp(0, new AnnotationRect(100, 100, 200, 60), imagePath, "APPROVED", "#00AA00", when),
        };

        await _service.ExportAsync(source, annotations, target, default);

        File.Exists(target).Should().BeTrue();

        WithDocument(target, doc =>
        {
            var stamp = ReadAnnotations(doc)[0].Should().ContainSingle().Subject;
            stamp.Subtype.Should().Be(SubtypeStamp);
            // /Contents retains the fallback label for accessibility/search.
            stamp.Contents.Should().Be("APPROVED");
            AssertRect(stamp.Rect, left: 100, bottom: 100, right: 300, top: 160);

            // Image-stamp path attaches an FPDF_PAGEOBJ_IMAGE inside the annotation; text-only
            // stamp attaches a path (border) + text. Distinguish by walking annotation objects.
            int imageObjects = CountAnnotationObjectsOfType(doc, pageIndex: 0, fpdfType: FPDF_PAGEOBJ_IMAGE);
            imageObjects.Should().BeGreaterThan(0, "image-stamp should attach at least one image page-object");
        });
    }

    private string MakeSampleImage(int wPx, int hPx)
    {
        using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(wPx, hPx);
        img.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    row[x] = new SixLabors.ImageSharp.PixelFormats.Rgba32(
                        (byte)((x * 4) % 256), (byte)((y * 8) % 256), 128, 255);
                }
            }
        });
        string path = Path.Combine(_tmpDir, $"stamp-{Guid.NewGuid():N}.png");
        SixLabors.ImageSharp.ImageExtensions.SaveAsPng(img, path);
        return path;
    }

    // FPDF_PAGEOBJ_IMAGE = 3 per public/fpdf_edit.h.
    private const int FPDF_PAGEOBJ_IMAGE = 3;

    private static int CountAnnotationObjectsOfType(FpdfDocumentT doc, int pageIndex, int fpdfType)
    {
        int total = 0;
        var page = fpdfview.FPDF_LoadPage(doc, pageIndex);
        try
        {
            int annotCount = fpdf_annot.FPDFPageGetAnnotCount(page);
            for (int i = 0; i < annotCount; i++)
            {
                var annot = fpdf_annot.FPDFPageGetAnnot(page, i);
                try
                {
                    int objCount = fpdf_annot.FPDFAnnotGetObjectCount(annot);
                    for (int j = 0; j < objCount; j++)
                    {
                        var obj = fpdf_annot.FPDFAnnotGetObject(annot, j);
                        if (fpdf_edit.FPDFPageObjGetType(obj) == fpdfType)
                        {
                            total++;
                        }
                    }
                }
                finally
                {
                    fpdf_annot.FPDFPageCloseAnnot(annot);
                }
            }
        }
        finally
        {
            fpdfview.FPDF_ClosePage(page);
        }
        return total;
    }

    [Fact]
    public async Task Export_LinePolygonArrow_AreSkipped_NotInOutput()
    {
        // PDFium 146.x limitation: AnnotationToPdfSpec.Map returns null for these kinds.
        // Они дропаются на app-layer и не попадают в /Annots; раунд-трип через FDF/XFDF/JSON
        // покрывает их полностью.
        string source = SourcePath();
        string target = Path.Combine(_tmpDir, "annotated-lines.pdf");
        var when = DateTimeOffset.UnixEpoch;

        var annotations = new[]
        {
            Annotation.Line(0, [new(10, 10), new(50, 50)], "#000", when),
            Annotation.Arrow(1, [new(10, 10), new(50, 50)], "#000", when),
            Annotation.Polygon(2, [new(10, 10), new(20, 10), new(15, 20)], "#000", when),
        };

        await _service.ExportAsync(source, annotations, target, default);

        File.Exists(target).Should().BeTrue();

        WithDocument(target, doc =>
        {
            var byPage = ReadAnnotations(doc);
            int total = byPage.Values.Sum(p => p.Count);
            total.Should().Be(0, "PDFium 146.x cannot embed /L /Vertices — these specs map to null");
        });
    }

    [Fact]
    public async Task Export_EmptyAnnotations_ProducesValidCopyWithSamePageCount()
    {
        string source = SourcePath();
        string target = Path.Combine(_tmpDir, "copy.pdf");

        await _service.ExportAsync(source, [], target, default);

        File.Exists(target).Should().BeTrue();

        WithDocument(target, doc =>
        {
            fpdfview.FPDF_GetPageCount(doc).Should().Be(SourcePageCount(source));
        });
    }

    private static int SourcePageCount(string path) => WithDocumentResult(path, fpdfview.FPDF_GetPageCount);

    private static IReadOnlyDictionary<int, List<AnnotationReadback>> ReadAnnotations(FpdfDocumentT doc)
    {
        var result = new Dictionary<int, List<AnnotationReadback>>();
        int pageCount = fpdfview.FPDF_GetPageCount(doc);
        for (int pi = 0; pi < pageCount; pi++)
        {
            var list = new List<AnnotationReadback>();
            var page = fpdfview.FPDF_LoadPage(doc, pi);
            try
            {
                int count = fpdf_annot.FPDFPageGetAnnotCount(page);
                for (int i = 0; i < count; i++)
                {
                    var annot = fpdf_annot.FPDFPageGetAnnot(page, i);
                    try
                    {
                        list.Add(ReadOne(annot));
                    }
                    finally
                    {
                        fpdf_annot.FPDFPageCloseAnnot(annot);
                    }
                }
            }
            finally
            {
                fpdfview.FPDF_ClosePage(page);
            }

            result[pi] = list;
        }

        return result;
    }

    private static AnnotationReadback ReadOne(FpdfAnnotationT annot)
    {
        int subtype = fpdf_annot.FPDFAnnotGetSubtype(annot);

        using var rect = new FS_RECTF_();
        fpdf_annot.FPDFAnnotGetRect(annot, rect);

        uint r = 0, g = 0, b = 0, a = 0;
        fpdf_annot.FPDFAnnotGetColor(annot, FPDFANNOT_COLORTYPE.FPDFANNOT_COLORTYPE_Color, ref r, ref g, ref b, ref a);

        return new AnnotationReadback(
            subtype,
            new RectF(rect.Left, rect.Bottom, rect.Right, rect.Top),
            r,
            g,
            b,
            ReadString(annot, "Contents"));
    }

    private static string? ReadString(FpdfAnnotationT annot, string key)
    {
        ulong len = fpdf_annot.FPDFAnnotGetStringValue(annot, key, ref Unsafe.NullRef<ushort>(), 0);
        if (len <= 2)
        {
            return null; // empty value is just the trailing UTF-16 NUL (2 bytes).
        }

        int units = (int)(len / 2);
        ushort[] buffer = new ushort[units];
        fpdf_annot.FPDFAnnotGetStringValue(annot, key, ref buffer[0], len);

        // Drop the trailing NUL unit.
        char[] chars = new char[units - 1];
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = (char)buffer[i];
        }

        return new string(chars);
    }

    private static void WithDocument(string path, Action<FpdfDocumentT> body) =>
        WithDocumentResult(path, doc =>
        {
            body(doc);
            return 0;
        });

    private static T WithDocumentResult<T>(string path, Func<FpdfDocumentT, T> body)
    {
        fpdfview.FPDF_InitLibrary();
        byte[] bytes = File.ReadAllBytes(path);
        GCHandle pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var doc = fpdfview.FPDF_LoadMemDocument64(pin.AddrOfPinnedObject(), (ulong)bytes.LongLength, null);
            doc.Should().NotBeNull("exported PDF must be openable by PDFium");
            try
            {
                return body(doc);
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

    private sealed record AnnotationReadback(
        int Subtype, RectF Rect, uint ColorR, uint ColorG, uint ColorB, string? Contents);

    private sealed record RectF(float Left, float Bottom, float Right, float Top);
}
