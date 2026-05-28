using System.Globalization;
using System.Runtime.InteropServices;
using Foliant.Application.Services;
using Foliant.Domain;
using PDFiumCore;

namespace Foliant.Engines.Pdf;

/// <summary>
/// PDFium-наложение текстового watermark'а: один text-page-object на страницу с матрицей
/// поворота вокруг центра страницы. Работает поверх той же блокировки <see cref="NativeGate"/>,
/// что и <see cref="AnnotatedPdfExportService"/> (PDFium не потокобезопасен между документами),
/// и пишет результат атомарно (temp + Move).
///
/// Phase 1: только текст, все страницы. Color/opacity/angle/size — из <see cref="WatermarkSpec"/>.
/// </summary>
public sealed class PdfiumWatermarkService : IWatermarkService
{
    private static readonly Lock NativeGate = new();

    public async Task ApplyAsync(string sourcePath, WatermarkSpec spec, string targetPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.Text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spec.FontSize);
        ArgumentOutOfRangeException.ThrowIfNegative(spec.Opacity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(spec.Opacity, 1.0);

        byte[] source = await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);
        byte[] output = await Task.Run(() => StampCore(source, spec, ct), ct).ConfigureAwait(false);
        await WriteAtomicAsync(targetPath, output, ct).ConfigureAwait(false);
    }

    private static byte[] StampCore(byte[] source, WatermarkSpec spec, CancellationToken ct)
    {
        lock (NativeGate)
        {
            PdfLibrary.EnsureInitialized();

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
                    int pageCount = fpdfview.FPDF_GetPageCount(doc);
                    for (int i = 0; i < pageCount; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        StampPage(doc, i, spec);
                    }

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

    private static void StampPage(FpdfDocumentT doc, int pageIndex, WatermarkSpec spec)
    {
        var page = fpdfview.FPDF_LoadPage(doc, pageIndex);
        if (page is null)
        {
            return;
        }

        try
        {
            float pageW = fpdfview.FPDF_GetPageWidthF(page);
            float pageH = fpdfview.FPDF_GetPageHeightF(page);

            // Standard-14: Helvetica — гарантированно есть в любом PDF reader'е без embedding.
            // PDFium ждёт fontName в ASCII; русский watermark рендерится через стандартный
            // ASCII-encoded Helvetica только для латиницы. Поддержка cyrillic font'ов = follow-up.
            var textObj = fpdf_edit.FPDFPageObjNewTextObj(doc, "Helvetica", (float)spec.FontSize);
            if (textObj is null)
            {
                return;
            }

            SetText(textObj, spec.Text);

            // Цвет + alpha. Opacity = 0..1 → 0..255.
            uint alpha = (uint)Math.Clamp((int)Math.Round(spec.Opacity * 255.0), 0, 255);
            fpdf_edit.FPDFPageObjSetFillColor(textObj, spec.R, spec.G, spec.B, alpha);

            // Матрица: поворот на angle CCW вокруг центра страницы + смещение текста к центру.
            // Width текста аппроксимируется как fontSize × 0.5 × len(text) (среднее для
            // proportional-шрифта). Это сдвинет центр строки в центр страницы.
            double angleRad = spec.AngleDegrees * Math.PI / 180.0;
            double cos = Math.Cos(angleRad);
            double sin = Math.Sin(angleRad);
            double textWidth = spec.FontSize * 0.5 * spec.Text.Length;
            double textHalfHeight = spec.FontSize * 0.5;
            double tx = pageW / 2.0 - cos * textWidth / 2.0 + sin * textHalfHeight;
            double ty = pageH / 2.0 - sin * textWidth / 2.0 - cos * textHalfHeight;

            using var matrix = new FS_MATRIX_
            {
                A = (float)cos,
                B = (float)sin,
                C = (float)(-sin),
                D = (float)cos,
                E = (float)tx,
                F = (float)ty,
            };
            fpdf_edit.FPDFPageObjSetMatrix(textObj, matrix);

            // Owning: после InsertObject документ владеет text-объектом.
            fpdf_edit.FPDFPageInsertObject(page, textObj);

            // Регенерируем content stream — без этого изменения не попадут в сохраняемый PDF.
            fpdf_edit.FPDFPageGenerateContent(page);
        }
        finally
        {
            fpdfview.FPDF_ClosePage(page);
        }
    }

    private static void SetText(FpdfPageobjectT textObj, string value)
    {
        // FPDFTextSetText: UTF-16LE с trailing NUL.
        ushort[] buffer = new ushort[value.Length + 1];
        for (int i = 0; i < value.Length; i++)
        {
            buffer[i] = value[i];
        }

        fpdf_edit.FPDFTextSetText(textObj, ref buffer[0]);
    }

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

            return 1;
        };

        if (fpdf_save.FPDF_SaveAsCopy(doc, writer, 0) == 0)
        {
            throw new InvalidOperationException("PDFium FPDF_SaveAsCopy failed.");
        }

        GC.KeepAlive(writer);
        return sink.ToArray();
    }

    private static async Task WriteAtomicAsync(string targetPath, byte[] bytes, CancellationToken ct)
    {
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
            // best-effort
        }
        catch (UnauthorizedAccessException)
        {
            // best-effort
        }
    }
}
