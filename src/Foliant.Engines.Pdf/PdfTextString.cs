using System.Globalization;
using System.Text;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Сериализует .NET-строку в PDF text-string literal (ISO 32000-1 §7.9.2): чистый ASCII →
/// <c>(literal)</c> со экранированием <c>( ) \</c>; всё остальное (кириллица, emoji) →
/// <c>&lt;hex&gt;</c> в UTF-16BE с BOM. Тот же паттерн, что в <see cref="PdfAnnotationCosWriter"/>
/// и <see cref="PdfDictionaryCosWriter"/>, выделен в общий helper, чтобы /Title outline'а и
/// /Contents аннотаций кодировались идентично.
/// </summary>
internal static class PdfTextString
{
    public static void Append(StringBuilder sb, string value)
    {
        ArgumentNullException.ThrowIfNull(sb);
        ArgumentNullException.ThrowIfNull(value);

        if (IsAsciiSafe(value))
        {
            AppendLiteral(sb, value);
            return;
        }

        AppendUtf16BeHex(sb, value);
    }

    private static void AppendLiteral(StringBuilder sb, string value)
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
    }

    private static void AppendUtf16BeHex(StringBuilder sb, string value)
    {
        sb.Append('<');
        AppendHexBytes(sb, Encoding.BigEndianUnicode.GetPreamble());
        AppendHexBytes(sb, Encoding.BigEndianUnicode.GetBytes(value));
        sb.Append('>');
    }

    private static void AppendHexBytes(StringBuilder sb, byte[] bytes)
    {
        foreach (byte b in bytes)
        {
            sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }
    }

    private static bool IsAsciiSafe(string value)
    {
        foreach (char c in value)
        {
            if (c < 0x20 || c > 0x7E)
            {
                return false;
            }
        }

        return true;
    }
}
