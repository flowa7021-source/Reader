using System.Globalization;
using System.Text;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Минимальный сериализатор PdfPig-токенов в cos-словарь PDF для инкрементального апдейта
/// (<see cref="PdfPigAnnotationAppender"/>). Покрывает только то, что встречается в /Page
/// словаре PDFium-выхода: dict, array, name, number, bool, indirect ref, hex/string. Stream-объекты
/// page-словарь не содержит (контент-стрим хранится отдельно через /Contents N 0 R).
/// </summary>
internal static class PdfDictionaryCosWriter
{
    /// <summary>Берёт исходный /Page словарь, перезаписывает /Annots объединением
    /// <paramref name="existingAnnots"/> + <paramref name="newAnnotRefs"/> (как inline-массив
    /// со ссылками), всё остальное копирует без изменений.</summary>
    public static string WritePageDictWithExtendedAnnots(
        DictionaryToken pageDict, ArrayToken existingAnnots, IReadOnlyList<IndirectReference> newAnnotRefs)
    {
        ArgumentNullException.ThrowIfNull(pageDict);
        ArgumentNullException.ThrowIfNull(existingAnnots);
        ArgumentNullException.ThrowIfNull(newAnnotRefs);

        var merged = new List<IToken>(existingAnnots.Length + newAnnotRefs.Count);
        merged.AddRange(existingAnnots.Data);
        foreach (var r in newAnnotRefs)
        {
            merged.Add(new IndirectReferenceToken(r));
        }

        var sb = new StringBuilder("<<\n");
        foreach (var kv in pageDict.Data)
        {
            if (string.Equals(kv.Key, NameToken.Annots.Data, StringComparison.Ordinal))
            {
                continue; // переписываем ниже
            }

            sb.Append('/').Append(kv.Key).Append(' ');
            WriteToken(sb, kv.Value);
            sb.Append('\n');
        }

        sb.Append("/Annots ");
        WriteArray(sb, merged);
        sb.Append('\n');
        sb.Append(">>");
        return sb.ToString();
    }

    private static void WriteToken(StringBuilder sb, IToken token)
    {
        switch (token)
        {
            case NameToken n:
                sb.Append('/').Append(n.Data);
                break;
            case NumericToken num:
                sb.Append(num.Data.ToString("0.######", CultureInfo.InvariantCulture));
                break;
            case BooleanToken b:
                sb.Append(b.Data ? "true" : "false");
                break;
            case NullToken:
                sb.Append("null");
                break;
            case IndirectReferenceToken iref:
                sb.Append(CultureInfo.InvariantCulture,
                    $"{iref.Data.ObjectNumber} {iref.Data.Generation} R");
                break;
            case ArrayToken arr:
                WriteArray(sb, arr.Data);
                break;
            case DictionaryToken d:
                WriteDictionary(sb, d);
                break;
            case StringToken s:
                WriteString(sb, s.Data);
                break;
            case HexToken h:
                WriteHexLiteral(sb, h);
                break;
            default:
                // Для незнакомых токенов — лучший вариант это null; иначе можно сломать PDF.
                sb.Append("null");
                break;
        }
    }

    private static void WriteArray(StringBuilder sb, IReadOnlyList<IToken> items)
    {
        sb.Append('[');
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            WriteToken(sb, items[i]);
        }

        sb.Append(']');
    }

    private static void WriteDictionary(StringBuilder sb, DictionaryToken d)
    {
        sb.Append("<< ");
        foreach (var kv in d.Data)
        {
            sb.Append('/').Append(kv.Key).Append(' ');
            WriteToken(sb, kv.Value);
            sb.Append(' ');
        }

        sb.Append(">>");
    }

    private static void WriteString(StringBuilder sb, string value)
    {
        // Любые non-ASCII выписываем hex-string в UTF-16BE с BOM (паттерн как в Annotation-writer'е).
        bool ascii = true;
        foreach (char c in value)
        {
            if (c < 0x20 || c > 0x7E)
            {
                ascii = false;
                break;
            }
        }

        if (ascii)
        {
            sb.Append('(');
            foreach (char c in value)
            {
                if (c is '(' or ')' or '\\')
                {
                    sb.Append('\\');
                }

                sb.Append(c);
            }

            sb.Append(')');
            return;
        }

        sb.Append('<');
        byte[] preamble = Encoding.BigEndianUnicode.GetPreamble();
        byte[] payload = Encoding.BigEndianUnicode.GetBytes(value);
        WriteHexBytes(sb, preamble);
        WriteHexBytes(sb, payload);
        sb.Append('>');
    }

    private static void WriteHexLiteral(StringBuilder sb, HexToken hex)
    {
        sb.Append('<');
        foreach (byte b in hex.Memory.Span)
        {
            sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }

        sb.Append('>');
    }

    private static void WriteHexBytes(StringBuilder sb, byte[] bytes)
    {
        foreach (byte b in bytes)
        {
            sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }
    }
}
