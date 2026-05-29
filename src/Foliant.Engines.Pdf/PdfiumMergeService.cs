using System.Globalization;
using System.Runtime.InteropServices;
using Foliant.Application.Services;
using PDFiumCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Foliant.Engines.Pdf;

/// <summary>
/// PDFium-объединение источников в PDF: создаёт пустой документ через <c>FPDF_CreateNewDocument</c>,
/// затем для каждого источника либо импортирует его страницы (<c>FPDF_ImportPages</c> для PDF),
/// либо встраивает как страницу-изображение (для PNG / JPEG / BMP / GIF / TIFF). Источники
/// открываются последовательно (PDFium не потокобезопасен между документами). Результат
/// пишется атомарно через temp + Move, как у <see cref="PdfiumWatermarkService"/>.
///
/// Размер image-страницы: <c>width_pixels × height_pixels</c> в PDF-точках (1 px = 1 pt).
/// Это даёт оригинальный визуальный размер на 72 DPI и предсказуемое поведение для
/// большинства image-источников. DjVu / EPUB как источники merge — отдельный PR.
/// </summary>
public sealed class PdfiumMergeService : IPdfMergeService
{
    private static readonly Lock NativeGate = new();

    /// <summary>Поддерживаемые image-расширения (case-insensitive, с точкой).</summary>
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tiff", ".tif",
    };

    public async Task MergeAsync(IReadOnlyList<string> sourcePaths, string targetPath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        if (sourcePaths.Count < 2)
        {
            throw new ArgumentException("At least two source paths required to merge.", nameof(sourcePaths));
        }
        foreach (var p in sourcePaths)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(p, nameof(sourcePaths));
        }

        // Load sources upfront. PDFs → raw bytes (used inside NativeGate-lock so we don't IO
        // there). Images → decoded BGRA32 + pixel dims (ImageSharp is fully managed, safe to
        // call before the lock).
        var sources = new List<MergeSource>(sourcePaths.Count);
        foreach (var path in sourcePaths)
        {
            ct.ThrowIfCancellationRequested();
            if (IsImage(path))
            {
                sources.Add(await LoadImageSourceAsync(path, ct).ConfigureAwait(false));
            }
            else
            {
                byte[] bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
                sources.Add(new MergeSource(MergeSourceKind.Pdf, bytes, 0, 0));
            }
        }

        byte[] output = await Task.Run(() => MergeCore(sources, ct), ct).ConfigureAwait(false);
        await WriteAtomicAsync(targetPath, output, ct).ConfigureAwait(false);
    }

    private static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path));

    private static async Task<MergeSource> LoadImageSourceAsync(string path, CancellationToken ct)
    {
        using var img = await Image.LoadAsync<Bgra32>(path, ct).ConfigureAwait(false);
        int wPx = img.Width;
        int hPx = img.Height;
        byte[] buffer = new byte[wPx * hPx * 4]; // BGRA32, stride = 4*w
        img.CopyPixelDataTo(buffer);
        return new MergeSource(MergeSourceKind.Image, buffer, wPx, hPx);
    }

    private static byte[] MergeCore(List<MergeSource> sources, CancellationToken ct)
    {
        lock (NativeGate)
        {
            PdfLibrary.EnsureInitialized();

            var dest = fpdf_edit.FPDF_CreateNewDocument();
            if (dest is null)
            {
                throw new InvalidOperationException("PDFium failed to create destination document.");
            }

            try
            {
                foreach (var source in sources)
                {
                    ct.ThrowIfCancellationRequested();
                    if (source.Kind == MergeSourceKind.Pdf)
                    {
                        AppendPdfSource(dest, source.Bytes);
                    }
                    else
                    {
                        AppendImageSource(dest, source.Bytes, source.WidthPx, source.HeightPx);
                    }
                }
                return Save(dest);
            }
            finally
            {
                fpdfview.FPDF_CloseDocument(dest);
            }
        }
    }

    private static void AppendPdfSource(FpdfDocumentT dest, byte[] sourceBytes)
    {
        GCHandle pin = GCHandle.Alloc(sourceBytes, GCHandleType.Pinned);
        try
        {
            var src = fpdfview.FPDF_LoadMemDocument64(pin.AddrOfPinnedObject(), (ulong)sourceBytes.LongLength, null);
            if (src is null)
            {
                var err = fpdfview.FPDF_GetLastError();
                throw new InvalidOperationException(
                    $"PDFium failed to load merge source: error {err.ToString(CultureInfo.InvariantCulture)}");
            }
            try
            {
                int currentDestCount = fpdfview.FPDF_GetPageCount(dest);
                if (fpdf_ppo.FPDF_ImportPages(dest, src, null, currentDestCount) == 0)
                {
                    throw new InvalidOperationException("PDFium FPDF_ImportPages returned failure.");
                }
            }
            finally
            {
                fpdfview.FPDF_CloseDocument(src);
            }
        }
        finally
        {
            pin.Free();
        }
    }

    /// <summary>Append a single image as a new page sized to its pixel dimensions (1 px → 1 pt).</summary>
    private static void AppendImageSource(FpdfDocumentT dest, byte[] bgra32, int wPx, int hPx)
    {
        GCHandle pin = GCHandle.Alloc(bgra32, GCHandleType.Pinned);
        try
        {
            // FPDFBitmap format 4 = BGRA. Stride = 4 * width for tightly-packed BGRA32.
            var bitmap = fpdfview.FPDFBitmapCreateEx(wPx, hPx, 4, pin.AddrOfPinnedObject(), wPx * 4);
            if (bitmap is null)
            {
                throw new InvalidOperationException("PDFium FPDFBitmapCreateEx failed for image source.");
            }

            try
            {
                int destPageIndex = fpdfview.FPDF_GetPageCount(dest);
                var page = fpdf_edit.FPDFPageNew(dest, destPageIndex, wPx, hPx);
                if (page is null)
                {
                    throw new InvalidOperationException("PDFium FPDFPageNew failed for image source.");
                }

                try
                {
                    var imgObj = fpdf_edit.FPDFPageObjNewImageObj(dest);
                    if (imgObj is null)
                    {
                        throw new InvalidOperationException("PDFium FPDFPageObjNewImageObj failed.");
                    }

                    if (fpdf_edit.FPDFImageObjSetBitmap(page, 1, imgObj, bitmap) == 0)
                    {
                        throw new InvalidOperationException("PDFium FPDFImageObjSetBitmap returned failure.");
                    }

                    // Image objects are by default a unit square; scale to page dimensions.
                    using var matrix = new FS_MATRIX_
                    {
                        A = wPx,
                        B = 0,
                        C = 0,
                        D = hPx,
                        E = 0,
                        F = 0,
                    };
                    fpdf_edit.FPDFPageObjSetMatrix(imgObj, matrix);

                    fpdf_edit.FPDFPageInsertObject(page, imgObj);
                    fpdf_edit.FPDFPageGenerateContent(page);
                }
                finally
                {
                    fpdfview.FPDF_ClosePage(page);
                }
            }
            finally
            {
                fpdfview.FPDFBitmapDestroy(bitmap);
            }
        }
        finally
        {
            pin.Free();
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
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private enum MergeSourceKind
    {
        Pdf,
        Image,
    }

    /// <summary>Internal envelope: either PDF bytes ready for <c>FPDF_LoadMemDocument64</c>,
    /// or decoded BGRA32 image with pixel dimensions.</summary>
    private sealed record MergeSource(MergeSourceKind Kind, byte[] Bytes, int WidthPx, int HeightPx);
}
