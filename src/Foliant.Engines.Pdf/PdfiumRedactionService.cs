using System.Globalization;
using System.Runtime.InteropServices;
using Foliant.Application.Services;
using Foliant.Domain;
using PDFiumCore;

namespace Foliant.Engines.Pdf;

/// <summary>
/// PDFium-реализация физического redaction'а (Q-F32 MVP). Для каждой <see cref="RedactionRegion"/>:
/// (1) удаляет текстовые page-object'ы, чей bbox пересекает прямоугольник — текст реально исчезает
/// из контента и текстового слоя (не просто закрывается); (2) рисует непрозрачный чёрный прямоугольник
/// поверх области. Работает поверх общей блокировки <see cref="NativeGate"/> (PDFium не потокобезопасен
/// между документами) и пишет результат атомарно (temp + Move) в новый файл — оригинал не мутируется.
///
/// Только координатные области + удаление пересекающего текста + чёрный бокс. Find-and-redact по
/// тексту/regex, удаление изображений, метаданные, OCG — follow-up.
/// </summary>
public sealed class PdfiumRedactionService : IRedactionService
{
    private static readonly Lock NativeGate = new();

    // FPDF_PAGEOBJ_TEXT — type-tag текстового page-object'а (см. fpdf_edit.h). Только такие
    // объекты удаляем в MVP; изображения/пути не трогаем.
    private const int TextObjectType = 1;

    // FPDFFILL=2 / без stroke=0 — режим отрисовки сплошного непрозрачного бокса.
    private const int FillModeFill = 2;
    private const int NoStroke = 0;

    public async Task RedactAsync(string sourcePath, string outputPath, IReadOnlyList<RedactionRegion> regions, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(regions);

        byte[] source = await File.ReadAllBytesAsync(sourcePath, ct).ConfigureAwait(false);
        byte[] output = await Task.Run(() => RedactCore(source, regions, ct), ct).ConfigureAwait(false);
        await WriteAtomicAsync(outputPath, output, ct).ConfigureAwait(false);
    }

    private static byte[] RedactCore(byte[] source, IReadOnlyList<RedactionRegion> regions, CancellationToken ct)
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
                    ApplyRegions(doc, regions, ct);
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

    /// <summary>Группирует области по странице (каждую страницу грузим один раз), валидирует
    /// индексы, применяет redaction. Пустой список → no-op (документ сохраняется как есть).</summary>
    private static void ApplyRegions(FpdfDocumentT doc, IReadOnlyList<RedactionRegion> regions, CancellationToken ct)
    {
        int pageCount = fpdfview.FPDF_GetPageCount(doc);
        foreach (var group in regions.GroupBy(r => r.PageIndex))
        {
            ct.ThrowIfCancellationRequested();
            int pageIndex = group.Key;
            if (pageIndex < 0 || pageIndex >= pageCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(regions),
                    $"RedactionRegion.PageIndex {pageIndex.ToString(CultureInfo.InvariantCulture)} is out of range " +
                    $"[0, {pageCount.ToString(CultureInfo.InvariantCulture)}).");
            }

            RedactPage(doc, pageIndex, [.. group]);
        }
    }

    private static void RedactPage(FpdfDocumentT doc, int pageIndex, IReadOnlyList<RedactionRegion> pageRegions)
    {
        var page = fpdfview.FPDF_LoadPage(doc, pageIndex);
        if (page is null)
        {
            return;
        }

        try
        {
            RemoveIntersectingText(page, pageRegions);

            // Чёрные боксы вставляем ПОСЛЕ удаления — иначе свежий бокс попал бы под сканирование
            // и мог быть удалён как пересекающий объект (rect — не text, но порядок важен и для GenerateContent).
            foreach (var region in pageRegions)
            {
                DrawBlackBox(doc, page, region.Rect);
            }

            // Регенерируем content stream — без этого мутации не попадут в сохраняемый PDF.
            fpdf_edit.FPDFPageGenerateContent(page);
        }
        finally
        {
            fpdfview.FPDF_ClosePage(page);
        }
    }

    /// <summary>Удаляет текстовые объекты, чьи bbox пересекают любую область. Сначала собираем
    /// объекты-жертвы (forward-pass), затем удаляем по хэндлу — удаление по индексу сдвигало бы
    /// последующие индексы. <c>FPDFPageRemoveObject</c> передаёт владение caller'у, поэтому
    /// detached-объект нужно явно освободить через <c>FPDFPageObjDestroy</c>.</summary>
    private static void RemoveIntersectingText(FpdfPageT page, IReadOnlyList<RedactionRegion> pageRegions)
    {
        int total = fpdf_edit.FPDFPageCountObjects(page);
        var victims = new List<FpdfPageobjectT>();
        for (int i = 0; i < total; i++)
        {
            var obj = fpdf_edit.FPDFPageGetObject(page, i);
            if (obj is null || fpdf_edit.FPDFPageObjGetType(obj) != TextObjectType)
            {
                continue;
            }

            if (IntersectsAny(obj, pageRegions))
            {
                victims.Add(obj);
            }
        }

        foreach (var obj in victims)
        {
            if (fpdf_edit.FPDFPageRemoveObject(page, obj) != 0)
            {
                fpdf_edit.FPDFPageObjDestroy(obj);
            }
        }
    }

    private static bool IntersectsAny(FpdfPageobjectT obj, IReadOnlyList<RedactionRegion> pageRegions)
    {
        float left = 0, bottom = 0, right = 0, top = 0;
        if (fpdf_edit.FPDFPageObjGetBounds(obj, ref left, ref bottom, ref right, ref top) == 0)
        {
            return false;
        }

        foreach (var region in pageRegions)
        {
            AnnotationRect r = region.Rect;
            // AABB-overlap в PDF user space (Y вверх): rect = (X, Y, X+W, Y+H).
            if (left < r.X + r.Width && right > r.X && bottom < r.Y + r.Height && top > r.Y)
            {
                return true;
            }
        }

        return false;
    }

    private static void DrawBlackBox(FpdfDocumentT doc, FpdfPageT page, AnnotationRect rect)
    {
        var box = fpdf_edit.FPDFPageObjCreateNewRect((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);
        if (box is null)
        {
            return;
        }

        // Непрозрачный чёрный заливочный прямоугольник.
        fpdf_edit.FPDFPageObjSetFillColor(box, 0u, 0u, 0u, 255u);
        fpdf_edit.FPDFPathSetDrawMode(box, FillModeFill, NoStroke);

        // Owning: после InsertObject документ владеет объектом.
        fpdf_edit.FPDFPageInsertObject(page, box);

        // doc может понадобиться для будущих режимов (шрифты и т.п.); сейчас bbox самодостаточен.
        GC.KeepAlive(doc);
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
