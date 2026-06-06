using System.Text;
using Foliant.Domain;
using UglyToad.PdfPig.Tokens;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Сканирует cos-структуру PDF на document-level скрипты / действия (ISO 32000-1 §12.6 / §12.7):
/// catalog <c>/OpenAction</c> (отличая JavaScript-действие от безопасного GoTo / destination-array),
/// document-level JavaScript name-tree <c>/Names → /JavaScript</c> (с рекурсией по <c>/Kids</c>) и
/// document additional-actions <c>/AA</c> на catalog'е. Симметрично
/// <see cref="PdfSanitizationCosWriter"/>'у. Учитывает только уровень catalog'а — per-page /
/// field-level <c>/AA</c> вне области (см. <c>IPdfSanitizationService</c>).
/// </summary>
internal static class PdfSanitizationCosReader
{
    private static readonly NameToken OpenActionName = NameToken.Create("OpenAction");
    private static readonly NameToken NamesName = NameToken.Create("Names");
    private static readonly NameToken JavaScriptName = NameToken.Create("JavaScript");
    private static readonly NameToken AaName = NameToken.Create("AA");
    private static readonly NameToken KidsName = NameToken.Create("Kids");
    private static readonly NameToken SName = NameToken.Create("S");

    /// <summary>Сканирует документ. Нет скриптов / действий уровня catalog'а → пустой отчёт
    /// (<see cref="PdfSanitizationReport.HasAnyJavaScriptOrActions"/> = <see langword="false"/>).
    /// Имена JavaScript-записей отсортированы (ordinal).</summary>
    public static PdfSanitizationReport Read(byte[] pdfBytes)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);

        using var doc = PdfPigDocument.Open(pdfBytes);
        var catalog = doc.Structure.Catalog.CatalogDictionary;

        bool hasJsOpenAction = HasJavaScriptOpenAction(doc, catalog);
        var jsNames = ReadDocumentJavaScriptNames(doc, catalog);
        bool hasAa = catalog.ContainsKey(AaName);

        return new PdfSanitizationReport(jsNames, hasJsOpenAction, hasAa);
    }

    private static bool HasJavaScriptOpenAction(PdfPigDocument doc, DictionaryToken catalog)
    {
        // /OpenAction — либо destination ARRAY ([3 0 R /Fit] — безопасный GoTo), либо action-словарь
        // << /S /JavaScript /JS (...) >>. Скриптом считаем только action-словарь с /S == /JavaScript.
        if (!TryResolveDict(doc, catalog, OpenActionName, out var action))
        {
            return false;
        }

        return action.TryGet(SName, out NameToken? s) && s is not null &&
            string.Equals(s.Data, JavaScriptName.Data, StringComparison.Ordinal);
    }

    private static List<string> ReadDocumentJavaScriptNames(PdfPigDocument doc, DictionaryToken catalog)
    {
        var sink = new List<string>();
        if (!TryResolveDict(doc, catalog, NamesName, out var names) ||
            !TryResolveDict(doc, names, JavaScriptName, out var tree))
        {
            return sink;
        }

        Walk(doc, tree, sink, depth: 0);
        sink.Sort(static (a, b) => string.CompareOrdinal(a, b));
        return sink;
    }

    private static void Walk(PdfPigDocument doc, DictionaryToken node, List<string> sink, int depth)
    {
        // Depth-guard against malformed/cyclic /Kids (StackOverflowException is uncatchable; see PdfCosLimits).
        if (depth > PdfCosLimits.MaxTreeDepth)
        {
            return;
        }

        if (node.TryGet(NamesName, out ArrayToken? names) && names is not null)
        {
            ReadNames(names, sink);
        }

        if (node.TryGet(KidsName, out ArrayToken? kids) && kids is not null)
        {
            foreach (var kid in kids.Data)
            {
                if (kid is IndirectReferenceToken kref &&
                    doc.Structure.GetObject(kref.Data) is ObjectToken { Data: DictionaryToken child })
                {
                    Walk(doc, child, sink, depth + 1);
                }
            }
        }
    }

    private static void ReadNames(ArrayToken names, List<string> sink)
    {
        // /Names — чередование [ key0 value0 key1 value1 … ]: key (i) — строка (имя JS-записи),
        // value (i+1) — JavaScript action-словарь. Нам нужны только имена-ключи.
        var data = names.Data;
        for (int i = 0; i + 1 < data.Count; i += 2)
        {
            string? name = ReadString(data[i]);
            if (name is not null)
            {
                sink.Add(name);
            }
        }
    }

    private static string? ReadString(IToken raw) => raw switch
    {
        HexToken h => DecodeHexString(h),
        StringToken str => str.Data,
        _ => null,
    };

    private static string DecodeHexString(HexToken hex)
    {
        // UTF-16BE с BOM (как пишет PdfTextString) декодируем явно из сырых байт — не полагаемся на
        // то, что нижележащий токенайзер уже распознал BOM (паттерн как в PdfAttachmentCosReader).
        var span = hex.Memory.Span;
        if (span.Length >= 2 && span[0] == 0xFE && span[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(span[2..]);
        }

        return hex.Data;
    }

    private static bool TryResolveDict(
        PdfPigDocument doc, DictionaryToken parent, NameToken key, out DictionaryToken dict)
    {
        dict = null!;
        if (!parent.TryGet(key, out IToken? raw) || raw is null)
        {
            return false;
        }

        switch (raw)
        {
            case DictionaryToken inline:
                dict = inline;
                return true;
            case IndirectReferenceToken iref
                when doc.Structure.GetObject(iref.Data) is ObjectToken { Data: DictionaryToken resolved }:
                dict = resolved;
                return true;
            default:
                return false;
        }
    }
}
