using System.Globalization;
using System.Text;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Тестовый билдер минимального PDF с <c>/OCProperties</c> для проверок
/// <see cref="PdfiumOcgService"/>. PdfPig'овский <c>PdfDocumentBuilder</c> не умеет OCG
/// из коробки, поэтому собираем cos-байт-в-байт вручную (паттерн <see cref="MinimalPdfFactory"/>).
///
/// Layout:
/// <list type="number">
/// <item><c>1</c> — Catalog (/Pages 2 0 R /OCProperties 3 0 R)</item>
/// <item><c>2</c> — Pages root (/Kids [PageObj]/Count 1)</item>
/// <item><c>3</c> — OCProperties (/OCGs [refs..]/D &lt;&lt; /BaseState /ON [/OFF [..]] &gt;&gt;)</item>
/// <item><c>4..3+N</c> — OCG dicts (/Type /OCG /Name (..))</item>
/// <item><c>4+N</c> — Page (/MediaBox [0 0 612 792])</item>
/// </list>
/// </summary>
internal static class OcgPdfFactory
{
    /// <summary>Записывает PDF в указанную директорию и возвращает путь. Все OCG —
    /// индирект'ы; <c>/OCProperties</c> — индирект; <c>/D</c> — inline в OCProperties.
    /// <paramref name="offIndices"/> определяет, какие 0-based слои попадают в <c>/D → /OFF</c>
    /// (default-hidden).</summary>
    public static string Write(string dir, string[] layerNames, int[] offIndices)
    {
        ArgumentNullException.ThrowIfNull(dir);
        ArgumentNullException.ThrowIfNull(layerNames);
        ArgumentNullException.ThrowIfNull(offIndices);

        int n = layerNames.Length;
        int firstOcgObj = 4;
        int pageObj = firstOcgObj + n;
        int totalObjects = pageObj;

        var sb = new StringBuilder();
        sb.Append("%PDF-1.5\n%\xE2\xE3\xCF\xD3\n");
        var offsets = new List<int> { 0 };

        AppendCatalog(sb, offsets, pagesObj: 2, ocpObj: 3);
        AppendPagesRoot(sb, offsets, pageObj);
        AppendOCProperties(sb, offsets, ocpObj: 3, firstOcgObj, n, offIndices);
        AppendOcgDicts(sb, offsets, firstOcgObj, layerNames);
        AppendPage(sb, offsets, pageObj);
        int xrefOffset = AppendXrefAndTrailer(sb, offsets, totalObjects, rootObj: 1);

        string path = Path.Combine(dir, "ocg-" + Guid.NewGuid().ToString("N") + ".pdf");
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes(sb.ToString()));
        _ = xrefOffset; // suppress unused-var
        return path;
    }

    private static void AppendCatalog(StringBuilder sb, List<int> offsets, int pagesObj, int ocpObj)
    {
        offsets.Add(sb.Length);
        sb.Append("1 0 obj\n<< /Type /Catalog /Pages ")
          .Append(pagesObj.ToString(CultureInfo.InvariantCulture))
          .Append(" 0 R /OCProperties ")
          .Append(ocpObj.ToString(CultureInfo.InvariantCulture))
          .Append(" 0 R >>\nendobj\n");
    }

    private static void AppendPagesRoot(StringBuilder sb, List<int> offsets, int pageObj)
    {
        offsets.Add(sb.Length);
        sb.Append("2 0 obj\n<< /Type /Pages /Kids [")
          .Append(pageObj.ToString(CultureInfo.InvariantCulture))
          .Append(" 0 R] /Count 1 >>\nendobj\n");
    }

    private static void AppendOCProperties(
        StringBuilder sb, List<int> offsets, int ocpObj, int firstOcgObj, int n, int[] offIndices)
    {
        offsets.Add(sb.Length);
        sb.Append(ocpObj.ToString(CultureInfo.InvariantCulture))
          .Append(" 0 obj\n<< /OCGs [");
        for (int i = 0; i < n; i++)
        {
            if (i > 0) { sb.Append(' '); }
            sb.Append((firstOcgObj + i).ToString(CultureInfo.InvariantCulture)).Append(" 0 R");
        }

        sb.Append("] /D << /BaseState /ON");
        if (offIndices.Length > 0)
        {
            sb.Append(" /OFF [");
            for (int i = 0; i < offIndices.Length; i++)
            {
                if (i > 0) { sb.Append(' '); }
                sb.Append((firstOcgObj + offIndices[i]).ToString(CultureInfo.InvariantCulture))
                  .Append(" 0 R");
            }

            sb.Append(']');
        }

        sb.Append(" >> >>\nendobj\n");
    }

    private static void AppendOcgDicts(
        StringBuilder sb, List<int> offsets, int firstOcgObj, string[] layerNames)
    {
        for (int i = 0; i < layerNames.Length; i++)
        {
            offsets.Add(sb.Length);
            sb.Append((firstOcgObj + i).ToString(CultureInfo.InvariantCulture))
              .Append(" 0 obj\n<< /Type /OCG /Name (")
              .Append(EscapeNameLiteral(layerNames[i]))
              .Append(") >>\nendobj\n");
        }
    }

    private static void AppendPage(StringBuilder sb, List<int> offsets, int pageObj)
    {
        offsets.Add(sb.Length);
        sb.Append(pageObj.ToString(CultureInfo.InvariantCulture))
          .Append(" 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>\nendobj\n");
    }

    private static int AppendXrefAndTrailer(StringBuilder sb, List<int> offsets, int totalObjects, int rootObj)
    {
        int xrefOffset = sb.Length;
        sb.Append("xref\n0 ").Append((totalObjects + 1).ToString(CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("0000000000 65535 f \n");
        for (int i = 1; i <= totalObjects; i++)
        {
            sb.Append(offsets[i].ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        sb.Append("trailer\n<< /Size ")
          .Append((totalObjects + 1).ToString(CultureInfo.InvariantCulture))
          .Append(" /Root ")
          .Append(rootObj.ToString(CultureInfo.InvariantCulture))
          .Append(" 0 R >>\nstartxref\n")
          .Append(xrefOffset.ToString(CultureInfo.InvariantCulture))
          .Append("\n%%EOF\n");
        return xrefOffset;
    }

    private static string EscapeNameLiteral(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (c is '(' or ')' or '\\')
            {
                sb.Append('\\');
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
}
