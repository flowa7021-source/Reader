using System.Globalization;
using System.Text;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Переписывает catalog (/Root) словарь, проставляя <c>/PageLabels</c>: либо ссылку на новый
/// number-tree объект, либо удаляя ключ (когда диапазонов нет). Все прочие ключи копируются как есть
/// через <see cref="PdfDictionaryCosWriter.WriteAnyToken"/>. Зеркально
/// <see cref="PdfCatalogOutlineCosWriter"/>'у — отдельный writer, т.к. ключ и семантика разные.
/// </summary>
internal static class PdfCatalogPageLabelCosWriter
{
    private const string PageLabelsKey = "PageLabels";

    /// <summary>Копирует <paramref name="catalog"/>, перезаписывая <c>/PageLabels</c>. Если
    /// <paramref name="pageLabelsRef"/> = <see langword="null"/> — ключ опускается (исходная
    /// нумерация больше не достижима из catalog'а), иначе пишется <c>/PageLabels N 0 R</c>.</summary>
    public static string WriteCatalogWithPageLabels(DictionaryToken catalog, IndirectReference? pageLabelsRef)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var sb = new StringBuilder("<<\n");
        foreach (var kv in catalog.Data)
        {
            if (string.Equals(kv.Key, PageLabelsKey, StringComparison.Ordinal))
            {
                continue; // переписываем (или опускаем) ниже
            }

            sb.Append('/').Append(kv.Key).Append(' ');
            PdfDictionaryCosWriter.WriteAnyToken(sb, kv.Value);
            sb.Append('\n');
        }

        if (pageLabelsRef is { } root)
        {
            sb.Append(CultureInfo.InvariantCulture, $"/PageLabels {root.ObjectNumber} {root.Generation} R\n");
        }

        sb.Append(">>");
        return sb.ToString();
    }
}
