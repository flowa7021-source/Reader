using System.Globalization;
using System.Runtime.InteropServices;
using Foliant.Application.Services;
using PDFiumCore;

namespace Foliant.Engines.Pdf;

/// <summary>
/// PDFium-объединение PDF: создаёт пустой документ через <c>FPDF_CreateNewDocument</c>,
/// затем для каждого источника вызывает <c>FPDF_ImportPages(dest, src, null, dest.PageCount)</c>,
/// что копирует все страницы в конец. Источники открываются последовательно (PDFium
/// не потокобезопасен между документами). Результат пишется атомарно через temp + Move,
/// как у <see cref="PdfiumWatermarkService"/>.
/// </summary>
public sealed class PdfiumMergeService : IPdfMergeService
{
    private static readonly Lock NativeGate = new();

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

        // Read all sources first so the lock-block only does CPU/native work; PDFium's load
        // function takes either a path or a pinned in-memory buffer — buffer keeps it tidy.
        var buffers = new List<byte[]>(sourcePaths.Count);
        foreach (var path in sourcePaths)
        {
            ct.ThrowIfCancellationRequested();
            buffers.Add(await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false));
        }

        byte[] output = await Task.Run(() => MergeCore(buffers, ct), ct).ConfigureAwait(false);
        await WriteAtomicAsync(targetPath, output, ct).ConfigureAwait(false);
    }

    private static byte[] MergeCore(List<byte[]> buffers, CancellationToken ct)
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
                foreach (var buffer in buffers)
                {
                    ct.ThrowIfCancellationRequested();
                    AppendSource(dest, buffer);
                }
                return Save(dest);
            }
            finally
            {
                fpdfview.FPDF_CloseDocument(dest);
            }
        }
    }

    private static void AppendSource(FpdfDocumentT dest, byte[] sourceBytes)
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
                // pagerange=null → all pages; index=currentDestCount → append.
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
}
