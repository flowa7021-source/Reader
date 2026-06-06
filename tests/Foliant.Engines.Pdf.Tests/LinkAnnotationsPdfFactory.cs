using System.Globalization;
using System.Text;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Строит валидный 2+-страничный PDF, где у первой страницы массив <c>/Annots</c> несёт link-аннотации
/// (ISO 32000-1 §12.5.6.5) — форму, которую удобнее собрать вручную, чем рендерить. Поддерживаемые виды
/// ссылок задаются флагами и покрывают все ветки <see cref="PdfLinkCosReader"/>'а: URI-action, GoTo-action
/// (<c>/D [pageRef /Fit]</c>), прямой <c>/Dest [pageRef /Fit]</c> и прямой <c>/Dest (name)</c> (именованный
/// пункт — MVP не разрешает). Каждая аннотация выносится в отдельный indirect-объект (проверяет резолв
/// indirect-аннотаций). Структура (xref/trailer) — как в <see cref="LegacyDestsPdfFactory"/>.
/// </summary>
internal static class LinkAnnotationsPdfFactory
{
    private const int CatalogObj = 1;
    private const int PagesObj = 2;
    private const int FirstPageObj = 3;
    private const int DefaultPageCount = 2;
    private const string Uri = "https://example.com";

    private const int GoToTargetPageIndex = 1; // GoTo / direct-dest array целятся на 2-ю страницу (index 1).

    /// <summary>
    /// Создаёт фикстуру с <paramref name="includeUriLink"/> URI-ссылкой, <paramref name="includeGoToLink"/>
    /// GoTo-ссылкой (на страницу index 1), <paramref name="includeDirectDestArrayLink"/> прямой
    /// <c>/Dest [pageRef /Fit]</c>-ссылкой (на страницу index 1) и <paramref name="includeNamedDestLink"/>
    /// прямой <c>/Dest (name)</c>-ссылкой на странице 0. Аннотации добавляются в указанном порядке.
    /// </summary>
    public static byte[] Create(
        bool includeUriLink = true,
        bool includeGoToLink = true,
        bool includeDirectDestArrayLink = false,
        bool includeNamedDestLink = false)
    {
        var enc = Encoding.Latin1;
        var objects = BuildObjects(
            includeUriLink, includeGoToLink, includeDirectDestArrayLink, includeNamedDestLink);

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
        bool includeUriLink, bool includeGoToLink, bool includeDirectDestArrayLink, bool includeNamedDestLink)
    {
        var kids = new StringBuilder();
        for (int i = 0; i < DefaultPageCount; i++)
        {
            kids.Append(CultureInfo.InvariantCulture, $"{FirstPageObj + i} 0 R ");
        }

        // Аннотации идут после страниц; собираем их тела и ссылки на них для /Annots первой страницы.
        int nextObj = FirstPageObj + DefaultPageCount;
        var annotBodies = new List<(int ObjNumber, string Body)>();
        var annotRefs = new StringBuilder();
        int goToTargetObj = FirstPageObj + GoToTargetPageIndex;

        if (includeUriLink)
        {
            AddAnnot(annotBodies, annotRefs, ref nextObj,
                $"/Subtype/Link/Rect[0 0 100 100]/A<</S/URI/URI({Uri})>>");
        }

        if (includeGoToLink)
        {
            AddAnnot(annotBodies, annotRefs, ref nextObj,
                $"/Subtype/Link/Rect[0 100 100 200]/A<</S/GoTo/D[{goToTargetObj} 0 R /Fit]>>");
        }

        if (includeDirectDestArrayLink)
        {
            AddAnnot(annotBodies, annotRefs, ref nextObj,
                $"/Subtype/Link/Rect[0 200 100 300]/Dest[{goToTargetObj} 0 R /Fit]");
        }

        if (includeNamedDestLink)
        {
            AddAnnot(annotBodies, annotRefs, ref nextObj,
                "/Subtype/Link/Rect[0 300 100 400]/Dest(somename)");
        }

        var objects = new List<(int, string)>
        {
            (CatalogObj, $"{CatalogObj} 0 obj\n<</Type/Catalog/Pages {PagesObj} 0 R>>\nendobj\n"),
            (PagesObj,
                $"{PagesObj} 0 obj\n<</Type/Pages/Kids[{kids.ToString().TrimEnd()}]/Count {DefaultPageCount}>>\nendobj\n"),
        };

        for (int i = 0; i < DefaultPageCount; i++)
        {
            string annotsEntry = i == 0 && annotRefs.Length > 0
                ? $"/Annots[{annotRefs.ToString().TrimEnd()}]"
                : string.Empty;
            string body =
                $"{FirstPageObj + i} 0 obj\n" +
                $"<</Type/Page/Parent {PagesObj} 0 R/MediaBox[0 0 595 {800 + i}]{annotsEntry}>>\n" +
                "endobj\n";
            objects.Add((FirstPageObj + i, body));
        }

        objects.AddRange(annotBodies);
        return objects;
    }

    private static void AddAnnot(
        List<(int ObjNumber, string Body)> annotBodies,
        StringBuilder annotRefs,
        ref int nextObj,
        string dictBody)
    {
        int obj = nextObj++;
        annotBodies.Add((obj, $"{obj} 0 obj\n<<{dictBody}>>\nendobj\n"));
        annotRefs.Append(CultureInfo.InvariantCulture, $"{obj} 0 R ");
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
