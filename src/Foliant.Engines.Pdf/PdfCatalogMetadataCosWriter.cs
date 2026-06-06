using System.Globalization;
using System.Text;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Переписывает catalog (/Root) словарь, проставляя <c>/Metadata</c> ссылкой на новый
/// metadata-поток (ISO 32000-1 §14.3.2). Все прочие ключи копируются как есть через
/// <see cref="PdfDictionaryCosWriter.WriteAnyToken"/>. Зеркально
/// <see cref="PdfCatalogPageLabelCosWriter"/>'у — отдельный writer, т.к. ключ (<c>/Metadata</c>) и
/// семантика (metadata-поток) другие.
/// </summary>
internal static class PdfCatalogMetadataCosWriter
{
    private const string MetadataKey = "Metadata";

    /// <summary>Копирует <paramref name="catalog"/>, перезаписывая <c>/Metadata</c> ссылкой
    /// <paramref name="metadataRef"/> на новый metadata-поток (старый, если был, перестаёт быть
    /// достижим из catalog'а). Все прочие ключи копируются как есть.</summary>
    public static string WriteCatalogWithMetadata(DictionaryToken catalog, IndirectReference metadataRef)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var sb = new StringBuilder("<<\n");
        foreach (var kv in catalog.Data)
        {
            if (string.Equals(kv.Key, MetadataKey, StringComparison.Ordinal))
            {
                continue; // переписываем ниже
            }

            sb.Append('/').Append(kv.Key).Append(' ');
            PdfDictionaryCosWriter.WriteAnyToken(sb, kv.Value);
            sb.Append('\n');
        }

        sb.Append(CultureInfo.InvariantCulture, $"/Metadata {metadataRef.ObjectNumber} {metadataRef.Generation} R\n");
        sb.Append(">>");
        return sb.ToString();
    }
}
