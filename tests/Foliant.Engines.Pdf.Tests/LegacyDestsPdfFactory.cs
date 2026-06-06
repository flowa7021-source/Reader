using System.Globalization;
using System.Text;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Строит валидный многостраничный PDF с <b>legacy</b>-словарём именованных пунктов назначения
/// (<c>Catalog → /Dests</c>, ISO 32000-1 §12.3.2) — формой, которую <c>PDFium</c> не генерирует, но
/// которую <see cref="PdfNamedDestinationCosReader"/> обязан читать. Каждый пункт указывает на страницу
/// destination-массивом <c>[pageRef /Fit]</c>. Используется в roundtrip-тестах, чтобы доказать, что
/// reader видит legacy-форму. Структура (xref/trailer) — как в <see cref="MultiPagePdfFactory"/>.
/// </summary>
internal static class LegacyDestsPdfFactory
{
    private const int CatalogObj = 1;
    private const int PagesObj = 2;
    private const int FirstPageObj = 3;

    /// <summary>
    /// Создаёт PDF с <paramref name="pageCount"/> пустыми страницами и legacy <c>/Dests</c>-словарём из
    /// <paramref name="dests"/> (имя → 0-based индекс целевой страницы).
    /// </summary>
    public static byte[] Create(int pageCount, IReadOnlyList<(string Name, int PageIndex)> dests)
    {
        ArgumentNullException.ThrowIfNull(dests);
        if (pageCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pageCount), pageCount, "Need at least one page.");
        }

        var enc = Encoding.Latin1;
        var objects = BuildObjects(pageCount, dests);

        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");

        int totalObjects = objects.Count;
        var offsets = new int[totalObjects + 1]; // 1-based object numbers
        foreach ((int objNumber, string body) in objects)
        {
            offsets[objNumber] = enc.GetByteCount(sb.ToString());
            sb.Append(body);
        }

        int xrefStart = enc.GetByteCount(sb.ToString());
        AppendXref(sb, offsets, totalObjects);
        AppendTrailer(sb, totalObjects, xrefStart);

        return enc.GetBytes(sb.ToString());
    }

    private static List<(int ObjNumber, string Body)> BuildObjects(
        int pageCount, IReadOnlyList<(string Name, int PageIndex)> dests)
    {
        var kids = new StringBuilder();
        for (int i = 0; i < pageCount; i++)
        {
            kids.Append(CultureInfo.InvariantCulture, $"{FirstPageObj + i} 0 R ");
        }

        var destsDict = new StringBuilder("<<");
        foreach (var (name, pageIndex) in dests)
        {
            int clamped = Math.Clamp(pageIndex, 0, pageCount - 1);
            destsDict.Append(CultureInfo.InvariantCulture, $"/{name} [{FirstPageObj + clamped} 0 R /Fit]");
        }

        destsDict.Append(">>");

        var objects = new List<(int, string)>
        {
            (CatalogObj,
                $"{CatalogObj} 0 obj\n<</Type/Catalog/Pages {PagesObj} 0 R/Dests {destsDict}>>\nendobj\n"),
            (PagesObj,
                $"{PagesObj} 0 obj\n<</Type/Pages/Kids[{kids.ToString().TrimEnd()}]/Count {pageCount}>>\nendobj\n"),
        };

        for (int i = 0; i < pageCount; i++)
        {
            string body =
                $"{FirstPageObj + i} 0 obj\n" +
                $"<</Type/Page/Parent {PagesObj} 0 R/MediaBox[0 0 595 {800 + i}]>>\n" +
                "endobj\n";
            objects.Add((FirstPageObj + i, body));
        }

        return objects;
    }

    private static void AppendXref(StringBuilder sb, int[] offsets, int totalObjects)
    {
        sb.Append("xref\n");
        sb.Append(CultureInfo.InvariantCulture, $"0 {totalObjects + 1}\n");
        sb.Append("0000000000 65535 f \n");
        for (int obj = 1; obj <= totalObjects; obj++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{offsets[obj]:D10} 00000 n \n");
        }
    }

    private static void AppendTrailer(StringBuilder sb, int totalObjects, int xrefStart)
    {
        sb.Append(CultureInfo.InvariantCulture,
            $"trailer\n<</Size {totalObjects + 1}/Root {CatalogObj} 0 R>>\nstartxref\n{xrefStart}\n%%EOF\n");
    }
}
