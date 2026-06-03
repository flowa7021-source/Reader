using System.Text;
using UglyToad.PdfPig.Tokens;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Сериализатор catalog'а (PDF root /Catalog dictionary) с inline-подменой
/// <c>/OCProperties</c>. Используется <see cref="PdfiumOcgService"/>'ом в редком кейсе,
/// когда исходный PDF держит <c>/OCProperties</c> inline в catalog'е (а не индирект'ом):
/// тогда обновление одной только инкрементальной перезаписи OCProperties недостаточно —
/// нужно переписать catalog с новым телом /OCProperties внутри.
///
/// Большинство Acrobat-сгенерированных PDF держат <c>/OCProperties</c> индиректом; этот
/// путь — fallback для нестандартных генераторов.
/// </summary>
internal static class PdfCatalogCosWriter
{
    private const string OCPropertiesKey = "OCProperties";

    /// <summary>Берёт оригинальный catalog-словарь, копирует все ключи кроме <c>/OCProperties</c>,
    /// и встраивает переданное pre-rendered тело <paramref name="ocPropertiesInlineBody"/>
    /// под ключом <c>/OCProperties</c>. Тело должно начинаться с <c>&lt;&lt;</c> и заканчиваться
    /// на <c>&gt;&gt;</c> (то есть быть готовым inline-dictionary literal'ом).</summary>
    public static string WriteCatalogWithInlineOCProperties(
        PdfOcgCosReader.OcgSnapshot snapshot, string ocPropertiesInlineBody)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(ocPropertiesInlineBody);

        var sb = new StringBuilder("<<\n");
        foreach (var kv in snapshot.CatalogDict.Data)
        {
            if (string.Equals(kv.Key, OCPropertiesKey, StringComparison.Ordinal))
            {
                continue; // переписываем ниже
            }

            sb.Append('/').Append(kv.Key).Append(' ');
            PdfDictionaryCosWriter.WriteAnyToken(sb, kv.Value);
            sb.Append('\n');
        }

        sb.Append("/OCProperties ");
        sb.Append(ocPropertiesInlineBody);
        sb.Append('\n');
        sb.Append(">>");
        return sb.ToString();
    }
}
