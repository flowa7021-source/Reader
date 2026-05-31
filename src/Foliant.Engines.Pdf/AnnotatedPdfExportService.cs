using System.Globalization;
using System.Runtime.InteropServices;
using Foliant.Application.Services;
using Foliant.Domain;
using PDFiumCore;

namespace Foliant.Engines.Pdf;

/// <summary>
/// PDFium-реализация <see cref="IAnnotatedPdfExportService"/>: читает исходный PDF, добавляет
/// настоящие редактируемые annotation-объекты (<c>/Highlight</c>, <c>/Underline</c>,
/// <c>/StrikeOut</c>, <c>/Text</c>, <c>/Ink</c>) через нативный слой PDFium и атомарно пишет
/// новый PDF. Чистый маппинг доменных аннотаций в числовой,
/// PDF-нейтральный вид делает <see cref="AnnotationToPdfSpec"/>; здесь — только нативная запись.
///
/// PDFium не потокобезопасен (даже между документами), поэтому весь нативный участок выполняется
/// под общим статическим замком <see cref="NativeGate"/>, а синхронная работа обёрнута в
/// <see cref="Task.Run{TResult}(Func{TResult}, CancellationToken)"/> ради неблокирующего IO и отмены.
/// </summary>
public sealed class AnnotatedPdfExportService : IAnnotatedPdfExportService
{
    // PDFium FPDF_ANNOTATION_SUBTYPE (см. public/fpdf_annot.h). В PDFiumCore 146.x именованные
    // константы не экспонируются, поэтому значения зашиты по спецификации PDFium.
    private const int SubtypeText = 1;
    private const int SubtypeHighlight = 9;
    private const int SubtypeUnderline = 10;
    private const int SubtypeStrikeout = 12;
    private const int SubtypeInk = 15;

    // FPDF_PAGEOBJ_* draw-mode для path-объекта чернил: только обводка, без заливки.
    private const int PathStrokeOnly = 1;
    private const float InkStrokeWidth = 1.5f;

    private static readonly Lock NativeGate = new();

    public async Task ExportAsync(
        string sourcePdfPath,
        IReadOnlyList<Annotation> annotations,
        string targetPath,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sourcePdfPath);
        ArgumentNullException.ThrowIfNull(annotations);
        ArgumentNullException.ThrowIfNull(targetPath);

        IReadOnlyList<PdfAnnotationSpec> specs = AnnotationToPdfSpec.MapMany(annotations);
        byte[] source = await File.ReadAllBytesAsync(sourcePdfPath, ct).ConfigureAwait(false);
        byte[] output = await Task.Run(() => Embed(source, specs, ct), ct).ConfigureAwait(false);
        await WriteAtomicAsync(targetPath, output, ct).ConfigureAwait(false);
    }

    private static byte[] Embed(byte[] source, IReadOnlyList<PdfAnnotationSpec> specs, CancellationToken ct)
    {
        lock (NativeGate)
        {
            PdfLibrary.EnsureInitialized();

            // FPDF_LoadMemDocument64 не копирует буфер — он должен жить до FPDF_CloseDocument,
            // поэтому пинуем массив на время работы с документом.
            GCHandle pin = GCHandle.Alloc(source, GCHandleType.Pinned);
            try
            {
                var doc = fpdfview.FPDF_LoadMemDocument64(pin.AddrOfPinnedObject(), (ulong)source.LongLength, null);
                if (doc is null)
                {
                    var err = fpdfview.FPDF_GetLastError();
                    throw new InvalidOperationException(
                        $"PDFium failed to load source document: error {err.ToString(CultureInfo.InvariantCulture)}");
                }

                try
                {
                    ApplyAnnotations(doc, specs, ct);
                    return Save(doc);
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
    }

    private static void ApplyAnnotations(
        FpdfDocumentT doc, IReadOnlyList<PdfAnnotationSpec> specs, CancellationToken ct)
    {
        // Группируем по странице, чтобы открыть каждую страницу один раз и один раз сгенерировать
        // её appearance-streams (FPDFPage_GenerateContent).
        foreach (var group in specs.GroupBy(s => s.PageIndex))
        {
            ct.ThrowIfCancellationRequested();

            var page = fpdfview.FPDF_LoadPage(doc, group.Key);
            if (page is null)
            {
                // Спецификация может ссылаться на несуществующую страницу — пропускаем, как и
                // прочие невалидные аннотации (контракт: invalid → skip).
                continue;
            }

            try
            {
                PdfPageBox box = ReadPageBox(page);
                bool any = false;
                foreach (var spec in group)
                {
                    ct.ThrowIfCancellationRequested();
                    any |= AddAnnotation(doc, page, PdfPageCoordinateTransform.ToUserSpace(spec, box));
                }

                if (any)
                {
                    fpdf_edit.FPDFPageGenerateContent(page);
                }
            }
            finally
            {
                fpdfview.FPDF_ClosePage(page);
            }
        }
    }

    private static PdfPageBox ReadPageBox(FpdfPageT page)
    {
        // FPDFPageGetMediaBox: returns 0 on failure. PDFium считает с MediaBox в PDF user space
        // (origin/Rotate не применяет — то, что нужно для собственного transform'а).
        float l = 0;
        float bottom = 0;
        float r = 0;
        float t = 0;
        if (fpdf_transformpage.FPDFPageGetMediaBox(page, ref l, ref bottom, ref r, ref t) != 0)
        {
            int rotation = fpdf_edit.FPDFPageGetRotation(page);
            return new PdfPageBox(l, bottom, r, t, rotation);
        }

        // Fallback: если MediaBox недоступен, считаем страницу «канонической» (origin 0,0, без
        // rotate). Это сохраняет старое поведение для странных PDF, а не падает.
        float w = fpdfview.FPDF_GetPageWidthF(page);
        float h = fpdfview.FPDF_GetPageHeightF(page);
        return PdfPageBox.Identity(w, h);
    }

    private static bool AddAnnotation(FpdfDocumentT doc, FpdfPageT page, PdfAnnotationSpec spec)
    {
        int subtype = spec.Subtype switch
        {
            PdfAnnotationSubtype.Highlight => SubtypeHighlight,
            PdfAnnotationSubtype.Text => SubtypeText,
            PdfAnnotationSubtype.Ink => SubtypeInk,
            PdfAnnotationSubtype.Underline => SubtypeUnderline,
            PdfAnnotationSubtype.Strikeout => SubtypeStrikeout,
            _ => -1,
        };

        if (subtype < 0)
        {
            return false;
        }

        var annot = fpdf_annot.FPDFPageCreateAnnot(page, subtype);
        if (annot is null)
        {
            return false;
        }

        try
        {
            // Spec уже в PDF user space благодаря PdfPageCoordinateTransform — учтены MediaBox
            // origin и /Rotate страницы. Прямая запись /Rect без доп. преобразований.
            using (var rect = ToRect(spec.Rect))
            {
                fpdf_annot.FPDFAnnotSetRect(annot, rect);
            }

            switch (spec.Subtype)
            {
                case PdfAnnotationSubtype.Highlight:
                case PdfAnnotationSubtype.Underline:
                case PdfAnnotationSubtype.Strikeout:
                    // Все три text-markup используют один путь: цвет + quadpoints. PDFium
                    // сам нарисует подходящий маркер (highlight fill / underline line / strikethrough line)
                    // на основе /Subtype.
                    SetColor(annot, spec.Color);
                    AppendQuadPoints(annot, spec.QuadPoints);
                    break;

                case PdfAnnotationSubtype.Text:
                    SetColor(annot, spec.Color);
                    SetStringValue(annot, "Contents", spec.Contents ?? string.Empty);
                    break;

                case PdfAnnotationSubtype.Ink:
                    // FPDFAnnot_AddInkStroke требует contiguous-массив FS_POINTF, а PDFiumCore 146.x
                    // не предоставляет публичного способа обернуть нативный буфер в FS_POINTF_
                    // (конструкторы по указателю — internal). Поэтому строим path-объект: цвет
                    // живёт на обводке штриха, а не в /C самой аннотации.
                    AppendInkPath(doc, annot, spec);
                    break;

                default:
                    return false;
            }

            WriteMetadata(annot, spec);
            return true;
        }
        finally
        {
            fpdf_annot.FPDFPageCloseAnnot(annot);
        }
    }

    private static void AppendInkPath(FpdfDocumentT doc, FpdfAnnotationT annot, PdfAnnotationSpec spec)
    {
        IReadOnlyList<AnnotationPoint>? pts = spec.InkPoints;
        if (pts is not { Count: >= 2 })
        {
            return;
        }

        var path = fpdf_edit.FPDFPageObjCreateNewPath((float)pts[0].X, (float)pts[0].Y);
        if (path is null)
        {
            return;
        }

        for (int i = 1; i < pts.Count; i++)
        {
            fpdf_edit.FPDFPathLineTo(path, (float)pts[i].X, (float)pts[i].Y);
        }

        var c = spec.Color;
        fpdf_edit.FPDFPageObjSetStrokeColor(path, c.R, c.G, c.B, c.A);
        fpdf_edit.FPDFPageObjSetStrokeWidth(path, InkStrokeWidth);
        fpdf_edit.FPDFPathSetDrawMode(path, fillmode: 0, stroke: PathStrokeOnly);

        // FPDFAnnot_AppendObject забирает владение path-объектом; уничтожать его вручную не нужно.
        fpdf_annot.FPDFAnnotAppendObject(annot, path);
    }

    private static void AppendQuadPoints(FpdfAnnotationT annot, IReadOnlyList<double>? quad)
    {
        // QuadPoints: [xTL,yTL, xTR,yTR, xBL,yBL, xBR,yBR] (8 doubles).
        if (quad is not { Count: 8 })
        {
            return;
        }

        using var q = new FS_QUADPOINTSF
        {
            X1 = (float)quad[0],
            Y1 = (float)quad[1],
            X2 = (float)quad[2],
            Y2 = (float)quad[3],
            X3 = (float)quad[4],
            Y3 = (float)quad[5],
            X4 = (float)quad[6],
            Y4 = (float)quad[7],
        };
        fpdf_annot.FPDFAnnotAppendAttachmentPoints(annot, q);
    }

    private static void SetColor(FpdfAnnotationT annot, PdfRgba color) =>
        fpdf_annot.FPDFAnnotSetColor(
            annot, FPDFANNOT_COLORTYPE.FPDFANNOT_COLORTYPE_Color, color.R, color.G, color.B, color.A);

    private static void WriteMetadata(FpdfAnnotationT annot, PdfAnnotationSpec spec)
    {
        // /CreationDate, /M — даты в PDF-формате "D:YYYYMMDDHHMMSSZ".
        // /T — автор; /Subj — тема. Все четыре опциональны; пишем только заполненные.
        if (spec.CreatedAt is { } created)
        {
            SetStringValue(annot, "CreationDate", PdfDateString(created));
        }

        if (spec.ModifiedAt is { } modified)
        {
            SetStringValue(annot, "M", PdfDateString(modified));
        }

        if (!string.IsNullOrEmpty(spec.Author))
        {
            SetStringValue(annot, "T", spec.Author);
        }

        if (!string.IsNullOrEmpty(spec.Subject))
        {
            SetStringValue(annot, "Subj", spec.Subject);
        }
    }

    private static string PdfDateString(DateTimeOffset when) =>
        "D:" + when.ToUniversalTime().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "Z";

    private static void SetStringValue(FpdfAnnotationT annot, string key, string value)
    {
        // PDFium ждёт UTF-16LE c завершающим NUL; ushort-буфер = code units строки .NET (тоже UTF-16).
        ushort[] buffer = new ushort[value.Length + 1];
        for (int i = 0; i < value.Length; i++)
        {
            buffer[i] = value[i];
        }

        fpdf_annot.FPDFAnnotSetStringValue(annot, key, ref buffer[0]);
    }

    private static FS_RECTF_ ToRect(PdfRect r) => new()
    {
        Left = (float)r.XLL,
        Bottom = (float)r.YLL,
        Right = (float)r.XUR,
        Top = (float)r.YUR,
    };

    private static byte[] Save(FpdfDocumentT doc)
    {
        using var sink = new MemoryStream();
        using var writer = new FPDF_FILEWRITE_ { Version = 1 };
        writer.WriteBlock = (_, data, size) =>
        {
            int len = (int)size;
            if (len > 0)
            {
                byte[] chunk = new byte[len];
                Marshal.Copy(data, chunk, 0, len);
                sink.Write(chunk, 0, len);
            }

            return 1; // non-zero == success
        };

        // FPDF_INCREMENTAL=1 / FPDF_NO_INCREMENTAL=2; 0 = библиотека решает сама.
        if (fpdf_save.FPDF_SaveAsCopy(doc, writer, 0) == 0)
        {
            throw new InvalidOperationException("PDFium FPDF_SaveAsCopy failed.");
        }

        // Удерживаем делегат живым до конца нативного вызова (защита от GC коллбэка).
        GC.KeepAlive(writer);
        return sink.ToArray();
    }

    private static async Task WriteAtomicAsync(string targetPath, byte[] bytes, CancellationToken ct)
    {
        // Атомарная запись: temp в той же папке (cross-volume Move не атомарен), затем Move overwrite.
        string dir = Path.GetDirectoryName(Path.GetFullPath(targetPath))!;
        Directory.CreateDirectory(dir);
        string tmp = Path.Combine(dir, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(tmp, bytes, ct).ConfigureAwait(false);
            File.Move(tmp, targetPath, overwrite: true);
        }
        finally
        {
            TryDelete(tmp);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // best-effort cleanup; nothing actionable on failure.
        }
        catch (UnauthorizedAccessException)
        {
            // best-effort cleanup; nothing actionable on failure.
        }
    }
}
