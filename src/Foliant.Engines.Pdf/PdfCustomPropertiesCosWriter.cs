using System.Text;
using Foliant.Domain;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Перезаписывает пользовательские (нестандартные) ключи <c>/Info</c> dictionary, эмитя обновлённую
/// версию существующего <c>/Info</c>-объекта инкрементальным апдейтом (симметрично
/// <see cref="PdfCustomPropertiesCosReader"/>'у). Стандартные 9 ключей
/// (<see cref="PdfCustomPropertiesCosReader.StandardInfoKeys"/>) копируются <b>дословно</b> через
/// <see cref="PdfDictionaryCosWriter.WriteAnyToken"/>, прежние custom-ключи отбрасываются, новые
/// пишутся как <c>/Name (value)</c> (<see cref="PdfTextString"/>). Номер объекта берётся из trailer'а
/// (<c>/Info N 0 R</c>) — обновлённая версия резолвится через xref, поэтому trailer переписывать не нужно.
///
/// <para><b>Ограничение:</b> если в источнике нет <c>/Info</c>-объекта, добавить его невозможно —
/// <see cref="PdfIncrementalWriter"/> (общая инфраструктура, не модифицируется здесь) пишет trailer без
/// <c>/Info</c>, так что новый объект остался бы недостижимым. В этом случае бросается
/// <see cref="NotSupportedException"/>. Оригинальные байты не меняются (ISO 32000-1 §7.5.6).</para>
/// </summary>
internal static class PdfCustomPropertiesCosWriter
{
    /// <summary>Эмитит обновлённый <c>/Info</c>-объект = (стандартные ключи дословно) + (переданные
    /// custom-ключи). Пустой <paramref name="customProps"/> → остаются только стандартные ключи.
    /// Возвращает байты нового PDF; <paramref name="source"/> не мутируется.</summary>
    /// <param name="source">Байты исходного PDF.</param>
    /// <param name="customProps">Полный новый набор пользовательских свойств (валидируется на дубликаты
    /// имён и пустые имена).</param>
    /// <returns>Байты нового PDF.</returns>
    /// <exception cref="ArgumentException">В <paramref name="customProps"/> есть дубликат имени
    /// (ordinal) либо пустое / whitespace имя.</exception>
    /// <exception cref="NotSupportedException">В источнике нет <c>/Info</c> dictionary.</exception>
    public static byte[] Write(byte[] source, IReadOnlyList<PdfCustomProperty> customProps)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(customProps);

        Validate(customProps);

        using var doc = PdfPigDocument.Open(source);
        var trailer = doc.Structure.CrossReferenceTable.Trailer;

        if (trailer.Info is not IndirectReferenceToken infoRefToken ||
            doc.Structure.GetObject(infoRefToken.Data) is not ObjectToken { Data: DictionaryToken infoDict })
        {
            throw new NotSupportedException(
                "Cannot write custom document properties: source has no /Info dictionary. " +
                "Set a standard field first (via IPdfMetadataEditService) so an /Info object exists.");
        }

        var infoRef = infoRefToken.Data;
        string body = WriteInfoDict(infoDict, customProps);
        var updated = new[] { new PdfIncrementalWriter.RawObject(infoRef, body) };

        long prevXref = PdfIncrementalWriter.FindLastStartXref(source);
        return PdfIncrementalWriter.Append(
            source, [], updated, prevXref, trailer.Root.ObjectNumber, trailer.Size);
    }

    private static void Validate(IReadOnlyList<PdfCustomProperty> customProps)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in customProps)
        {
            if (string.IsNullOrWhiteSpace(p.Name))
            {
                throw new ArgumentException(
                    "Custom property name must not be empty or whitespace.", nameof(customProps));
            }

            if (!seen.Add(p.Name))
            {
                throw new ArgumentException(
                    $"Duplicate custom property name '{p.Name}'.", nameof(customProps));
            }
        }
    }

    private static string WriteInfoDict(DictionaryToken infoDict, IReadOnlyList<PdfCustomProperty> customProps)
    {
        var sb = new StringBuilder("<<\n");

        // Стандартные ключи копируем дословно (включая их исходный тип токена), custom-ключи source'а
        // отбрасываем — их полностью задаёт переданный набор.
        foreach (var kv in infoDict.Data)
        {
            if (!PdfCustomPropertiesCosReader.StandardInfoKeys.Contains(kv.Key))
            {
                continue;
            }

            sb.Append('/').Append(EncodeName(kv.Key)).Append(' ');
            PdfDictionaryCosWriter.WriteAnyToken(sb, kv.Value);
            sb.Append('\n');
        }

        foreach (var p in customProps)
        {
            sb.Append('/').Append(EncodeName(p.Name)).Append(' ');
            PdfTextString.Append(sb, p.Value);
            sb.Append('\n');
        }

        sb.Append(">>");
        return sb.ToString();
    }

    private static string EncodeName(string name)
    {
        // PDF name (ISO 32000-1 §7.3.5): regular-символы пишем как есть, всё прочее (delimiters,
        // whitespace, '#', не-ASCII) — как #XX от UTF-8 байт. ASCII-имена типичны и проходят без замен.
        var sb = new StringBuilder(name.Length);
        foreach (byte b in Encoding.UTF8.GetBytes(name))
        {
            if (IsRegularNameByte(b))
            {
                sb.Append((char)b);
            }
            else
            {
                sb.Append('#').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return sb.ToString();
    }

    private static bool IsRegularNameByte(byte b) =>
        b is > 0x20 and < 0x7F
        && b is not ((byte)'#' or (byte)'/' or (byte)'%'
            or (byte)'(' or (byte)')' or (byte)'<' or (byte)'>'
            or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}');
}
