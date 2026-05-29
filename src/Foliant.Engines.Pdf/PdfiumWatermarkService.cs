using System.Globalization;
using System.Runtime.InteropServices;
using Foliant.Application.Services;
using Foliant.Domain;
using PDFiumCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Foliant.Engines.Pdf;

/// <summary>
/// PDFium-наложение watermark'а: текст (page-text-object с rotation matrix) ИЛИ изображение
/// (page-image-object с alpha-mult'ом и scale+rotation matrix). Работает поверх общей
/// блокировки <see cref="NativeGate"/> (PDFium не потокобезопасен между документами) и пишет
/// результат атомарно (temp + Move).
///
/// Спека определяется <see cref="WatermarkSpec.ImagePath"/>: задан → image-режим (color-каналы
/// игнорируются), не задан → text-режим. <see cref="WatermarkSpec.Range"/> в обоих режимах
/// определяет, на каких страницах наносить.
/// </summary>
public sealed class PdfiumWatermarkService : IWatermarkService
{
    private static readonly Lock NativeGate = new();

    /// <summary>Доля ширины страницы, занимаемая image-watermark'ом по большей стороне.
    /// 40 % — компромисс: достаточно видимо, не закрывает контент целиком.</summary>
    private const double ImageWatermarkPageWidthFraction = 0.4;

    public async Task ApplyAsync(string sourcePath, WatermarkSpec spec, string targetPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spec.FontSize);
        ArgumentOutOfRangeException.ThrowIfNegative(spec.Opacity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(spec.Opacity, 1.0);
        if (spec.ImagePath is null)
        {
            // Text mode requires non-blank text.
            ArgumentException.ThrowIfNullOrWhiteSpace(spec.Text);
        }

        byte[] source = await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);

        // Decode image upfront (managed work) when needed; pass BGRA32 buffer + dimensions
        // into the lock-block so the native section stays pure.
        ImagePayload? image = null;
        if (spec.ImagePath is not null)
        {
            image = await LoadImagePayloadAsync(spec.ImagePath, spec.Opacity, ct).ConfigureAwait(false);
        }

        byte[] output = await Task.Run(() => StampCore(source, spec, image, ct), ct).ConfigureAwait(false);
        await WriteAtomicAsync(targetPath, output, ct).ConfigureAwait(false);
    }

    private static async Task<ImagePayload> LoadImagePayloadAsync(string imagePath, double opacity, CancellationToken ct)
    {
        using var img = await Image.LoadAsync<Bgra32>(imagePath, ct).ConfigureAwait(false);
        int wPx = img.Width;
        int hPx = img.Height;
        byte[] buffer = new byte[wPx * hPx * 4];
        img.CopyPixelDataTo(buffer);

        // Apply opacity by scaling the alpha channel — viewers honor per-pixel alpha when
        // PDFium embeds the bitmap. Stride is tightly packed BGRA32: A is at byte 3 of each px.
        if (opacity < 1.0)
        {
            byte alphaScale = (byte)Math.Clamp((int)Math.Round(opacity * 255.0), 0, 255);
            for (int i = 3; i < buffer.Length; i += 4)
            {
                buffer[i] = (byte)(buffer[i] * alphaScale / 255);
            }
        }

        return new ImagePayload(buffer, wPx, hPx);
    }

    private static byte[] StampCore(byte[] source, WatermarkSpec spec, ImagePayload? image, CancellationToken ct)
    {
        lock (NativeGate)
        {
            PdfLibrary.EnsureInitialized();

            GCHandle pin = GCHandle.Alloc(source, GCHandleType.Pinned);
            GCHandle imagePin = image is not null
                ? GCHandle.Alloc(image.Bgra32, GCHandleType.Pinned)
                : default;
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
                    PageRange range = spec.Range ?? PageRange.All;
                    for (int i = 0; i < pageCount; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!range.Includes(i))
                        {
                            continue;
                        }
                        if (image is not null)
                        {
                            StampPageImage(doc, i, spec, image, imagePin.AddrOfPinnedObject());
                        }
                        else
                        {
                            StampPage(doc, i, spec);
                        }
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
                if (imagePin.IsAllocated)
                {
                    imagePin.Free();
                }
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

    /// <summary>Image-watermark путь: декодированный BGRA32 (с уже применённым alpha-mult'ом)
    /// привязывается к page-image-object'у, скейлится до ~40 % ширины страницы по большей
    /// стороне с сохранением aspect ratio, поворачивается на <see cref="WatermarkSpec.AngleDegrees"/>
    /// относительно центра, центруется на странице.</summary>
    private static void StampPageImage(FpdfDocumentT doc, int pageIndex, WatermarkSpec spec, ImagePayload image, IntPtr pinnedBuffer)
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

            // Целевой размер: больший из (width-fraction × page-width) и height-aware fit.
            double aspect = (double)image.HeightPx / image.WidthPx;
            double targetW = pageW * ImageWatermarkPageWidthFraction;
            double targetH = targetW * aspect;

            // Создаём bitmap, ссылающийся на наш пинированный BGRA-буфер.
            var bitmap = fpdfview.FPDFBitmapCreateEx(image.WidthPx, image.HeightPx, 4, pinnedBuffer, image.WidthPx * 4);
            if (bitmap is null)
            {
                return;
            }

            try
            {
                var imgObj = fpdf_edit.FPDFPageObjNewImageObj(doc);
                if (imgObj is null)
                {
                    return;
                }

                if (fpdf_edit.FPDFImageObjSetBitmap(page, 1, imgObj, bitmap) == 0)
                {
                    return;
                }

                // Матрица: image-object — единичный квадрат [0..1, 0..1]. Скейл до (targetW, targetH),
                // поворот вокруг центра скейленного rect'а, смещение к центру страницы.
                double angleRad = spec.AngleDegrees * Math.PI / 180.0;
                double cos = Math.Cos(angleRad);
                double sin = Math.Sin(angleRad);
                double cx = pageW / 2.0;
                double cy = pageH / 2.0;
                // Стандартная формула: компонуем scale + rotate + translate-to-center.
                double a = cos * targetW;
                double b = sin * targetW;
                double c = -sin * targetH;
                double d = cos * targetH;
                double e = cx - (cos * targetW / 2.0) + (sin * targetH / 2.0);
                double f = cy - (sin * targetW / 2.0) - (cos * targetH / 2.0);

                using var matrix = new FS_MATRIX_
                {
                    A = (float)a,
                    B = (float)b,
                    C = (float)c,
                    D = (float)d,
                    E = (float)e,
                    F = (float)f,
                };
                fpdf_edit.FPDFPageObjSetMatrix(imgObj, matrix);

                fpdf_edit.FPDFPageInsertObject(page, imgObj);
                fpdf_edit.FPDFPageGenerateContent(page);
            }
            finally
            {
                fpdfview.FPDFBitmapDestroy(bitmap);
            }
        }
        finally
        {
            fpdfview.FPDF_ClosePage(page);
        }
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

    /// <summary>Декодированный image-watermark: BGRA32 + размеры. Alpha-канал уже
    /// домножен на <see cref="WatermarkSpec.Opacity"/>.</summary>
    private sealed record ImagePayload(byte[] Bgra32, int WidthPx, int HeightPx);
}
