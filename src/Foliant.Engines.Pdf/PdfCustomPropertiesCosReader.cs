using System.Text;
using Foliant.Domain;
using UglyToad.PdfPig.Tokens;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Читает <b>пользовательские</b> (нестандартные) ключи <c>/Info</c> dictionary из cos-структуры PDF.
/// Источник <c>/Info</c> — trailer (<c>doc.Structure.CrossReferenceTable.Trailer.Info</c>, ISO 32000-1
/// §14.3.3 — ссылка идёт из trailer'а, а не из catalog'а), inline-словарь либо indirect-ссылка.
/// Стандартные 9 ключей (<see cref="StandardInfoKeys"/>) пропускаются (сравнение по PDF-имени,
/// чувствительно к регистру), каждое значение читается как PDF text-string (<see cref="StringToken"/> /
/// <see cref="HexToken"/>, UTF-16BE BOM-hex декодируется как в прочих reader'ах). Нет <c>/Info</c> →
/// пустой список. Симметрично <see cref="PdfCustomPropertiesCosWriter"/>'у.
/// </summary>
internal static class PdfCustomPropertiesCosReader
{
    /// <summary>Девять стандартных ключей <c>/Info</c> (ISO 32000-1 §14.3.3, таблица 317), которые
    /// ведает <c>IPdfMetadataEditService</c>; всё остальное в <c>/Info</c> — custom-свойство.</summary>
    internal static readonly IReadOnlySet<string> StandardInfoKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "Title", "Author", "Subject", "Keywords", "Creator", "Producer", "CreationDate", "ModDate", "Trapped",
    };

    /// <summary>Читает пользовательские свойства <c>/Info</c>. Нет <c>/Info</c> → пустой список.
    /// Результат отсортирован по <see cref="PdfCustomProperty.Name"/> (ordinal).</summary>
    /// <param name="pdfBytes">Байты PDF.</param>
    /// <returns>Пользовательские свойства (возможно пустой список).</returns>
    public static IReadOnlyList<PdfCustomProperty> Read(byte[] pdfBytes)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);

        using var doc = PdfPigDocument.Open(pdfBytes);
        if (!TryResolveInfoDict(doc, out var info))
        {
            return [];
        }

        var sink = new List<PdfCustomProperty>(info.Data.Count);
        foreach (var kv in info.Data)
        {
            if (StandardInfoKeys.Contains(kv.Key))
            {
                continue;
            }

            if (TryReadTextString(kv.Value, out string value))
            {
                sink.Add(new PdfCustomProperty(kv.Key, value));
            }
        }

        sink.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        return sink;
    }

    /// <summary>Резолвит <c>/Info</c>-словарь из trailer'а: <see langword="null"/>-токен (нет
    /// <c>/Info</c>) → <see langword="false"/>; inline-словарь либо indirect-ссылка → словарь.</summary>
    internal static bool TryResolveInfoDict(PdfPigDocument doc, out DictionaryToken info)
    {
        info = null!;
        IToken? raw = doc.Structure.CrossReferenceTable.Trailer.Info;
        switch (raw)
        {
            case DictionaryToken inline:
                info = inline;
                return true;
            case IndirectReferenceToken iref
                when doc.Structure.GetObject(iref.Data) is ObjectToken { Data: DictionaryToken resolved }:
                info = resolved;
                return true;
            default:
                return false;
        }
    }

    private static bool TryReadTextString(IToken token, out string value)
    {
        switch (token)
        {
            case StringToken s:
                value = s.Data;
                return true;
            case HexToken h:
                value = DecodeHexString(h);
                return true;
            default:
                value = string.Empty;
                return false;
        }
    }

    private static string DecodeHexString(HexToken hex)
    {
        // UTF-16BE с BOM (как пишет PdfTextString) декодируем явно из сырых байт — не полагаемся на
        // то, что нижележащий токенайзер уже распознал BOM (паттерн как в PdfPageLabelCosReader).
        var span = hex.Memory.Span;
        if (span.Length >= 2 && span[0] == 0xFE && span[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(span[2..]);
        }

        return hex.Data;
    }
}
