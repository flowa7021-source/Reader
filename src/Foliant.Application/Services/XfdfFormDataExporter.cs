using System.Xml.Linq;

namespace Foliant.Application.Services;

/// <summary>
/// Экспорт form-данных в XFDF (Adobe XML Forms Data Format) — стандартный обмен AcroForm-данных
/// (Q-F24). Структура:
/// <code>
/// &lt;xfdf&gt;
///   &lt;fields&gt;
///     &lt;field name="FullName"&gt;&lt;value&gt;Иван&lt;/value&gt;&lt;/field&gt;
///     …
///   &lt;/fields&gt;
/// &lt;/xfdf&gt;
/// </code>
/// Stateless, без I/O. Round-trip парой с <see cref="XfdfFormDataImporter"/>.
/// </summary>
public sealed class XfdfFormDataExporter : IFormDataExporter
{
    private static readonly XNamespace Ns = "http://ns.adobe.com/xfdf/";

    public string FormatName => "XFDF";

    public string FileExtension => "xfdf";

    public string Export(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var fields = new XElement(Ns + "fields");
        foreach (var kv in values)
        {
            // XFDF spec: <field name="..."><value>...</value></field>. Дочерний <value> может
            // быть пустым; пустой имя поля пропускаем — это служебная ошибка caller'а.
            if (string.IsNullOrEmpty(kv.Key))
            {
                continue;
            }

            fields.Add(new XElement(
                Ns + "field",
                new XAttribute("name", kv.Key),
                new XElement(Ns + "value", kv.Value ?? string.Empty)));
        }

        var root = new XElement(
            Ns + "xfdf",
            new XAttribute(XNamespace.Xml + "space", "preserve"),
            fields);

        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Environment.NewLine + root;
    }
}
