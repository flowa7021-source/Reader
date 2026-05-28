using System.Globalization;
using System.Runtime.InteropServices;
using Foliant.Application.Services;
using Foliant.Domain;
using PDFiumCore;

namespace Foliant.Engines.Pdf;

/// <summary>
/// PDFium-наложение колонтитулов: верх/низ страницы, центрированно по горизонтали, без поворота.
/// Один text-object на колонтитул на страницу. Placeholder'ы расширяются per-page; результат
/// пишется атомарно (temp + Move). NativeGate шарится с другими PDFium-сервисами.
/// </summary>
public sealed class PdfiumHeaderFooterService : IHeaderFooterService
{
    private static readonly Lock NativeGate = new();

    // Отступ от края страницы до базовой линии колонтитула, в PDF points. 18pt ≈ 6mm — типично
    // для документов; не настраивается в Phase 1, чтобы не плодить параметры.
    private const float Margin = 18f;

    public async Task ApplyAsync(string sourcePath, HeaderFooterSpec spec, string targetPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        if (string.IsNullOrWhiteSpace(spec.HeaderText) && string.IsNullOrWhiteSpace(spec.FooterText))
        {
            throw new ArgumentException("At least one of HeaderText / FooterText must be set.", nameof(spec));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spec.FontSize);

        string filename = Path.GetFileName(sourcePath);
        byte[] source = await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);
        byte[] output = await Task.Run(() => StampCore(source, spec, filename, ct), ct).ConfigureAwait(false);
        await WriteAtomicAsync(targetPath, output, ct).ConfigureAwait(false);
    }

    private static byte[] StampCore(byte[] source, HeaderFooterSpec spec, string filename, CancellationToken ct)
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
                    string today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                    for (int i = 0; i < pageCount; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        StampPage(doc, i, pageCount, spec, filename, today);
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

    private static void StampPage(FpdfDocumentT doc, int pageIndex, int totalPages, HeaderFooterSpec spec, string filename, string today)
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
            bool any = false;

            if (!string.IsNullOrWhiteSpace(spec.HeaderText))
            {
                string text = ExpandPlaceholders(spec.HeaderText!, pageIndex, totalPages, filename, today);
                AddCenteredText(doc, page, text, spec, pageW, y: pageH - Margin);
                any = true;
            }

            if (!string.IsNullOrWhiteSpace(spec.FooterText))
            {
                string text = ExpandPlaceholders(spec.FooterText!, pageIndex, totalPages, filename, today);
                AddCenteredText(doc, page, text, spec, pageW, y: Margin);
                any = true;
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

    private static void AddCenteredText(FpdfDocumentT doc, FpdfPageT page, string text, HeaderFooterSpec spec, float pageW, float y)
    {
        var textObj = fpdf_edit.FPDFPageObjNewTextObj(doc, "Helvetica", (float)spec.FontSize);
        if (textObj is null)
        {
            return;
        }

        SetText(textObj, text);
        fpdf_edit.FPDFPageObjSetFillColor(textObj, spec.R, spec.G, spec.B, 255u);

        // Width аппроксимируем как fontSize × 0.5 × len (средняя для proportional-шрифта).
        // Centering без знания реальных метрик: даёт визуально центральную позицию для типичных
        // строк. Для точности нужна font metric query — отложено вместе с font-embedding.
        double textWidth = spec.FontSize * 0.5 * text.Length;
        double tx = pageW / 2.0 - textWidth / 2.0;

        using var matrix = new FS_MATRIX_
        {
            A = 1f,
            B = 0f,
            C = 0f,
            D = 1f,
            E = (float)tx,
            F = y,
        };
        fpdf_edit.FPDFPageObjSetMatrix(textObj, matrix);

        fpdf_edit.FPDFPageInsertObject(page, textObj);
    }

    /// <summary>Расширяет placeholder'ы в шаблоне. Public для отдельного unit-тестирования
    /// без нативного PDFium.</summary>
    public static string ExpandPlaceholders(string template, int pageIndex, int totalPages, string filename, string today)
    {
        ArgumentNullException.ThrowIfNull(template);
        return template
            .Replace("{page}", (pageIndex + 1).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{total}", totalPages.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{filename}", filename, StringComparison.Ordinal)
            .Replace("{date}", today, StringComparison.Ordinal);
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
