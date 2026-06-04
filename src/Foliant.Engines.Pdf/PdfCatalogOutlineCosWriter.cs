using System.Globalization;
using System.Text;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Переписывает catalog (/Root) словарь, проставляя <c>/Outlines</c>: либо ссылку на новый
/// outline-root, либо удаляя ключ (когда entries пуст). Все прочие ключи копируются как есть через
/// <see cref="PdfDictionaryCosWriter.WriteAnyToken"/>. Зеркально <see cref="PdfCatalogCosWriter"/>'у
/// (тот подменяет /OCProperties) — отдельный writer, т.к. ключ и семантика разные.
/// </summary>
internal static class PdfCatalogOutlineCosWriter
{
    private const string OutlinesKey = "Outlines";

    /// <summary>Копирует <paramref name="catalog"/>, перезаписывая <c>/Outlines</c>. Если
    /// <paramref name="outlineRoot"/> = <see langword="null"/> — ключ опускается (исходный outline
    /// больше не достижим из catalog'а), иначе пишется <c>/Outlines N 0 R</c>.</summary>
    public static string WriteCatalogWithOutline(DictionaryToken catalog, IndirectReference? outlineRoot)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var sb = new StringBuilder("<<\n");
        foreach (var kv in catalog.Data)
        {
            if (string.Equals(kv.Key, OutlinesKey, StringComparison.Ordinal))
            {
                continue; // переписываем (или опускаем) ниже
            }

            sb.Append('/').Append(kv.Key).Append(' ');
            PdfDictionaryCosWriter.WriteAnyToken(sb, kv.Value);
            sb.Append('\n');
        }

        if (outlineRoot is { } root)
        {
            sb.Append(CultureInfo.InvariantCulture, $"/Outlines {root.ObjectNumber} {root.Generation} R\n");
        }

        sb.Append(">>");
        return sb.ToString();
    }
}
