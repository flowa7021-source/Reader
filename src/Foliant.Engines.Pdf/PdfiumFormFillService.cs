using System.Globalization;
using System.Runtime.InteropServices;
using Foliant.Application.Services;
using PDFiumCore;

namespace Foliant.Engines.Pdf;

/// <summary>
/// PDFium write-back для AcroForm: проходит по всем widget-аннотациям, сравнивает их
/// <c>/T</c> (partial field name) с переданным словарём, и для каждого совпадения пишет
/// <c>/V</c> через <c>FPDFAnnotSetStringValue</c>. Использует тот же <see cref="NativeGate"/>,
/// что и <see cref="PdfiumWatermarkService"/>, и атомарно сохраняет результат
/// (<c>FPDF_SaveAsCopy</c> + temp + Move).
///
/// Поля идентифицируются по <c>/T</c> на уровне терминальной widget-аннотации. Иерархические
/// формы Acrobat-стиля (контейнеры → терминалы) в текущей реализации не «склеиваются»:
/// мы матчим против partial name. Для большинства Phase-1 PDF этого достаточно — exporter'ы
/// FDF/XFDF тоже хранят имена «как есть».
/// </summary>
public sealed class PdfiumFormFillService : IPdfFormFillService
{
    private static readonly Lock NativeGate = new();

    /// <summary>PDF spec: <c>/Subtype /Widget</c> кодируется PDFium-константой <c>FPDF_ANNOT_WIDGET = 20</c>.</summary>
    private const int AnnotSubtypeWidget = 20;

    public async Task ApplyAsync(
        string sourcePath,
        IReadOnlyDictionary<string, string> values,
        string targetPath,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        byte[] source = await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);
        byte[] output = await Task.Run(() => FillCore(source, values, ct), ct).ConfigureAwait(false);
        await WriteAtomicAsync(targetPath, output, ct).ConfigureAwait(false);
    }

    private static byte[] FillCore(byte[] source, IReadOnlyDictionary<string, string> values, CancellationToken ct)
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
                        FillPage(doc, i, values);
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

    private static void FillPage(FpdfDocumentT doc, int pageIndex, IReadOnlyDictionary<string, string> values)
    {
        var page = fpdfview.FPDF_LoadPage(doc, pageIndex);
        if (page is null)
        {
            return;
        }

        try
        {
            int annotCount = fpdf_annot.FPDFPageGetAnnotCount(page);
            for (int j = 0; j < annotCount; j++)
            {
                var annot = fpdf_annot.FPDFPageGetAnnot(page, j);
                if (annot is null)
                {
                    continue;
                }

                try
                {
                    if (fpdf_annot.FPDFAnnotGetSubtype(annot) != AnnotSubtypeWidget)
                    {
                        continue;
                    }

                    string? fieldName = ReadStringEntry(annot, "T");
                    if (fieldName is null || !values.TryGetValue(fieldName, out string? value))
                    {
                        continue;
                    }

                    WriteStringEntry(annot, "V", value);
                }
                finally
                {
                    fpdf_annot.FPDFPageCloseAnnot(annot);
                }
            }

            fpdf_edit.FPDFPageGenerateContent(page);
        }
        finally
        {
            fpdfview.FPDF_ClosePage(page);
        }
    }

    /// <summary>Read a UTF-16LE string entry from an annotation dictionary; <c>null</c> if absent or empty.</summary>
    private static string? ReadStringEntry(FpdfAnnotationT annot, string key)
    {
        // First probe: pass empty buffer to learn required length (PDFium convention).
        ushort dummy = 0;
        ulong needed = fpdf_annot.FPDFAnnotGetStringValue(annot, key, ref dummy, 0);
        if (needed <= 2) // 2 bytes = trailing NUL only → entry absent or empty.
        {
            return null;
        }

        // needed is byte count including trailing NUL; allocate UTF-16 buffer.
        ushort[] buffer = new ushort[needed / 2];
        ulong written = fpdf_annot.FPDFAnnotGetStringValue(annot, key, ref buffer[0], needed);
        if (written < 2)
        {
            return null;
        }

        int charCount = (int)(written / 2) - 1; // strip trailing NUL
        var chars = new char[charCount];
        for (int i = 0; i < charCount; i++)
        {
            chars[i] = (char)buffer[i];
        }
        return new string(chars);
    }

    /// <summary>Write a UTF-16LE string entry to an annotation dictionary. Empty <paramref name="value"/>
    /// stores an empty <c>/V</c> entry; that matches what most viewers expect for «cleared» fields.</summary>
    private static void WriteStringEntry(FpdfAnnotationT annot, string key, string value)
    {
        // PDFium ждёт UTF-16LE с trailing NUL.
        ushort[] buffer = new ushort[value.Length + 1];
        for (int i = 0; i < value.Length; i++)
        {
            buffer[i] = value[i];
        }
        fpdf_annot.FPDFAnnotSetStringValue(annot, key, ref buffer[0]);
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
