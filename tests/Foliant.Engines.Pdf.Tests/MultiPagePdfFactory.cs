using System.Globalization;
using System.Text;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Строит валидный многостраничный PDF (N пустых страниц) с корректной
/// xref-таблицей. Нужен для roundtrip-тестов <see cref="PdfPageOps"/>, т.к.
/// штатный <c>MinimalPdfFactory</c> делает ровно одну страницу.
///
/// Порядок страниц в тестах проверяется по высоте MediaBox: страница i получает
/// высоту <c>baseHeightPt + i</c> — уникальный отпечаток, который легко сверить
/// после удаления/переупорядочивания/вставки, не прибегая к рендеру.
/// </summary>
internal static class MultiPagePdfFactory
{
    private const int CatalogObj = 1;
    private const int PagesObj = 2;
    private const int FirstPageObj = 3;

    public static byte[] Create(int pageCount, int widthPt = 595, int baseHeightPt = 800)
    {
        if (pageCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pageCount), pageCount, "Need at least one page.");
        }

        var enc = Encoding.Latin1;
        var objects = BuildObjects(pageCount, widthPt, baseHeightPt);

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

    /// <summary>Высота i-й страницы (0-based) — отпечаток порядка для проверок в тестах.</summary>
    public static int HeightOfPage(int index, int baseHeightPt = 800) => baseHeightPt + index;

    private static List<(int ObjNumber, string Body)> BuildObjects(int pageCount, int widthPt, int baseHeightPt)
    {
        var kids = new StringBuilder();
        for (int i = 0; i < pageCount; i++)
        {
            kids.Append(CultureInfo.InvariantCulture, $"{FirstPageObj + i} 0 R ");
        }

        var objects = new List<(int, string)>
        {
            (CatalogObj, $"{CatalogObj} 0 obj\n<</Type/Catalog/Pages {PagesObj} 0 R>>\nendobj\n"),
            (PagesObj, $"{PagesObj} 0 obj\n<</Type/Pages/Kids[{kids.ToString().TrimEnd()}]/Count {pageCount}>>\nendobj\n"),
        };

        for (int i = 0; i < pageCount; i++)
        {
            int height = HeightOfPage(i, baseHeightPt);
            string body =
                $"{FirstPageObj + i} 0 obj\n" +
                $"<</Type/Page/Parent {PagesObj} 0 R/MediaBox[0 0 {widthPt} {height}]>>\n" +
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
