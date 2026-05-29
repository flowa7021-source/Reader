using System.Globalization;
using System.Runtime.InteropServices;
using Foliant.Application.Services;
using Foliant.Domain;
using PDFiumCore;

namespace Foliant.Engines.Pdf;

/// <summary>
/// PDFium-обрезка страниц: для каждой страницы читает текущий MediaBox и выставляет новый
/// CropBox с трим-долями из <see cref="CropSpec"/>. Содержимое не модифицируется — viewer
/// просто перестаёт показывать срезанные поля. Работает поверх той же блокировки
/// <see cref="NativeGate"/>, что и <see cref="PdfiumWatermarkService"/>, и пишет атомарно.
/// </summary>
public sealed class PdfiumCropService : IPdfCropService
{
    private static readonly Lock NativeGate = new();

    public async Task ApplyAsync(string sourcePath, CropSpec spec, string targetPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        spec.Validate();
        if (!spec.HasEffect)
        {
            throw new ArgumentException("CropSpec has no effect (all sides ≤ 0.005).", nameof(spec));
        }

        byte[] source = await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);
        byte[] output = await Task.Run(() => CropCore(source, spec, ct), ct).ConfigureAwait(false);
        await WriteAtomicAsync(targetPath, output, ct).ConfigureAwait(false);
    }

    private static byte[] CropCore(byte[] source, CropSpec spec, CancellationToken ct)
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
                        CropPage(doc, i, spec);
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

    private static void CropPage(FpdfDocumentT doc, int pageIndex, CropSpec spec)
    {
        var page = fpdfview.FPDF_LoadPage(doc, pageIndex);
        if (page is null)
        {
            return;
        }

        try
        {
            // PDFium точки — origin в нижнем-левом углу; MediaBox = (left, bottom, right, top).
            // Если MediaBox прочитать не получилось — берём страничные размеры.
            float left = 0, bottom = 0, right = 0, top = 0;
            fpdf_transformpage.FPDFPageGetMediaBox(page, ref left, ref bottom, ref right, ref top);
            float width = right - left;
            float height = top - bottom;
            if (width <= 0 || height <= 0)
            {
                width = fpdfview.FPDF_GetPageWidthF(page);
                height = fpdfview.FPDF_GetPageHeightF(page);
                left = 0;
                bottom = 0;
                right = width;
                top = height;
            }

            float newLeft = left + (float)(spec.Left * width);
            float newRight = right - (float)(spec.Right * width);
            float newBottom = bottom + (float)(spec.Bottom * height);
            float newTop = top - (float)(spec.Top * height);

            fpdf_transformpage.FPDFPageSetCropBox(page, newLeft, newBottom, newRight, newTop);

            if (spec.Mode == CropMode.Physical)
            {
                // Физический режим: меняем и MediaBox, и добавляем page-level clip-path.
                // SetMediaBox делает PDF-страницу реально меньше; viewer'ы, игнорирующие
                // CropBox, всё равно видят только новые границы. Clip-path гарантирует,
                // что любой контент, нарисованный «за» новыми границами, не отображается.
                fpdf_transformpage.FPDFPageSetMediaBox(page, newLeft, newBottom, newRight, newTop);

                var clip = fpdf_transformpage.FPDF_CreateClipPath(newLeft, newBottom, newRight, newTop);
                if (clip is not null)
                {
                    try
                    {
                        fpdf_transformpage.FPDFPageInsertClipPath(page, clip);
                    }
                    finally
                    {
                        fpdf_transformpage.FPDF_DestroyClipPath(clip);
                    }
                }
            }

            fpdf_edit.FPDFPageGenerateContent(page);
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
}
