using System.Text;

namespace Foliant.Application.Services;

/// <summary>
/// Экспорт form-данных в FDF (Adobe Forms Data Format) — PDF-syntax-based бинарный-совместимый
/// контейнер. Acrobat умеет открыть FDF и применить значения к open'нутому PDF (классический
/// workflow «дайте мне форму, я вернул FDF, вы импортировали его в свою копию PDF»).
///
/// Структура:
/// <code>
/// %FDF-1.2
/// 1 0 obj
/// &lt;&lt;
/// /FDF &lt;&lt;
/// /Fields [
///   &lt;&lt; /T (FieldName) /V (FieldValue) &gt;&gt;
///   …
/// ]
/// &gt;&gt;
/// &gt;&gt;
/// endobj
/// trailer
/// &lt;&lt; /Root 1 0 R &gt;&gt;
/// %%EOF
/// </code>
///
/// Текстовые значения сериализуются как PDF text strings: UTF-16BE с BOM в hex-форме —
/// единственный portable способ для кириллицы. Round-trip парой с
/// <see cref="FdfFormDataImporter"/>.
/// </summary>
public sealed class FdfFormDataExporter : IFormDataExporter
{
    public string FormatName => "FDF";

    public string FileExtension => "fdf";

    public string Export(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var sb = new StringBuilder();
        sb.Append("%FDF-1.2\n1 0 obj\n<<\n/FDF\n<<\n/Fields [\n");
        foreach (var kv in values)
        {
            if (string.IsNullOrEmpty(kv.Key))
            {
                continue;
            }

            sb.Append("<< /T ").Append(PdfText(kv.Key))
              .Append(" /V ").Append(PdfText(kv.Value ?? string.Empty))
              .Append(" >>\n");
        }

        sb.Append("]\n>>\n>>\nendobj\ntrailer\n<<\n/Root 1 0 R\n>>\n%%EOF\n");
        return sb.ToString();
    }

    // PDF text string как UTF-16BE hex с BOM — единственный переносимый способ для не-ASCII.
    private static string PdfText(string text)
    {
        byte[] bytes = Encoding.BigEndianUnicode.GetBytes(text);
        var sb = new StringBuilder("<FEFF");
        foreach (byte b in bytes)
        {
            sb.Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return sb.Append('>').ToString();
    }
}
