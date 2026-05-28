using System.Xml.Linq;

namespace Foliant.Application.Services;

/// <summary>
/// Импорт form-данных из XFDF — обратная операция к <see cref="XfdfFormDataExporter"/>. Ищем
/// <c>&lt;field name="..."&gt;</c> по локальному имени независимо от namespace, чтобы принимать
/// XFDF из Acrobat и других инструментов. Битые/неполные элементы пропускаются.
/// </summary>
public sealed class XfdfFormDataImporter : IFormDataImporter
{
    public string FormatName => "XFDF";

    public string FileExtension => "xfdf";

    public IReadOnlyDictionary<string, string> Import(string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var doc = XDocument.Parse(content);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var field in doc.Descendants().Where(e => e.Name.LocalName == "field"))
        {
            string? name = field.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            // <value>...</value> — первое дочернее value-element. Adobe nested-fields (где field
            // содержит другие field'ы) тут разворачиваются как плоский список — для Q-F24 этого
            // достаточно; иерархия имени восстановится позже, когда будет mapping в PDFium.
            var valueElement = field.Elements().FirstOrDefault(e => e.Name.LocalName == "value");
            result[name] = valueElement?.Value ?? string.Empty;
        }

        return result;
    }
}
