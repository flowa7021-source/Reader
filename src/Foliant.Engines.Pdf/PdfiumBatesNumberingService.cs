using System.Globalization;
using System.Runtime.InteropServices;
using Foliant.Application.Services;
using Foliant.Domain;
using PDFiumCore;

namespace Foliant.Engines.Pdf;

/// <summary>
/// PDFium-наложение Bates-нумерации: один text-object на страницу в нижнем углу. Текст —
/// монотонный счётчик <c>{Prefix}{номер:D{Digits}}{Suffix}</c>, формируемый
/// <see cref="BatesNumberingSpec.FormatFor"/> по абсолютному индексу страницы (номера стабильны
/// независимо от <see cref="BatesNumberingSpec.Range"/>). Результат пишется атомарно (temp + Move);
/// оригинал не мутируется. NativeGate шарится с другими PDFium-сервисами.
/// </summary>
public sealed class PdfiumBatesNumberingService : IBatesNumberingService
{
    private static readonly Lock NativeGate = new();

    // Отступ от края страницы до базовой линии штампа, в PDF points. 18pt ≈ 6mm — типично
    // для нижнего колонтитула; не настраивается, чтобы не плодить параметры (паритет с Acrobat).
    private const float Margin = 18f;

    public async Task ApplyAsync(string sourcePath, BatesNumberingSpec spec, string targetPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spec.FontSize);
        ArgumentOutOfRangeException.ThrowIfLessThan(spec.Digits, 1);

        byte[] source = await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);
        byte[] output = await Task.Run(() => StampCore(source, spec, ct), ct).ConfigureAwait(false);
        await WriteAtomicAsync(targetPath, output, ct).ConfigureAwait(false);
    }

    private static byte[] StampCore(byte[] source, BatesNumberingSpec spec, CancellationToken ct)
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
                    return StampAllPages(doc, spec, ct);
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

    private static byte[] StampAllPages(FpdfDocumentT doc, BatesNumberingSpec spec, CancellationToken ct)
    {
        int pageCount = fpdfview.FPDF_GetPageCount(doc);
        PageRange range = spec.Range ?? PageRange.All;

        for (int i = 0; i < pageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            // Range фильтрует только нанесение; FormatFor(i) использует абсолютный индекс, поэтому
            // печать поддиапазона не сдвигает Bates-номера (юридическая стабильность).
            if (range.Includes(i))
            {
                StampPage(doc, i, spec);
            }
        }

        return Save(doc);
    }

    private static void StampPage(FpdfDocumentT doc, int pageIndex, BatesNumberingSpec spec)
    {
        var page = fpdfview.FPDF_LoadPage(doc, pageIndex);
        if (page is null)
        {
            return;
        }

        try
        {
            float pageW = fpdfview.FPDF_GetPageWidthF(page);
            string text = spec.FormatFor(pageIndex);
            if (AddText(doc, page, text, spec, pageW))
            {
                fpdf_edit.FPDFPageGenerateContent(page);
            }
        }
        finally
        {
            fpdfview.FPDF_ClosePage(page);
        }
    }

    /// <summary>Помещает Bates text-object в один из трёх нижних углов. Y — базовая линия у
    /// нижнего края с отступом <see cref="Margin"/>; X зависит от Left/Center/Right.
    /// Возвращает <c>true</c>, если объект создан и вставлен.</summary>
    private static bool AddText(FpdfDocumentT doc, FpdfPageT page, string text, BatesNumberingSpec spec, float pageW)
    {
        var textObj = fpdf_edit.FPDFPageObjNewTextObj(doc, "Helvetica", (float)spec.FontSize);
        if (textObj is null)
        {
            return false;
        }

        SetText(textObj, text);
        fpdf_edit.FPDFPageObjSetFillColor(textObj, spec.R, spec.G, spec.B, 255u);

        // Width аппроксимируем как fontSize × 0.5 × len (средняя для proportional-шрифта) —
        // тем же способом, что header/footer, для согласованного выравнивания по правому краю.
        double textWidth = spec.FontSize * 0.5 * text.Length;
        double tx = spec.Position switch
        {
            BatesPosition.BottomLeft => Margin,
            BatesPosition.BottomRight => pageW - Margin - textWidth,
            _ => pageW / 2.0 - textWidth / 2.0, // BottomCenter
        };

        using var matrix = new FS_MATRIX_ { A = 1f, B = 0f, C = 0f, D = 1f, E = (float)tx, F = Margin };
        fpdf_edit.FPDFPageObjSetMatrix(textObj, matrix);
        fpdf_edit.FPDFPageInsertObject(page, textObj);
        return true;
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
