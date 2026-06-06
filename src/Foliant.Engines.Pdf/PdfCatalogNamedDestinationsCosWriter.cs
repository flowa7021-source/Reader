using System.Globalization;
using System.Text;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Переписывает catalog (/Root) словарь и его подсловарь <c>/Names</c> при работе с именованными
/// пунктами назначения: <c>/Dests</c> внутри <c>/Names</c> заменяется ссылкой на новый name-tree, а
/// прочие подключи (<c>/EmbeddedFiles</c>, <c>/JavaScript</c>, …) и прочие ключи catalog'а копируются
/// как есть через <see cref="PdfDictionaryCosWriter.WriteAnyToken"/>. Зеркально
/// <see cref="PdfCatalogNamesCosWriter"/>'у — отдельный writer, т.к. перезаписываемый подключ
/// (<c>/Dests</c>) другой.
/// </summary>
internal static class PdfCatalogNamedDestinationsCosWriter
{
    private const string NamesKey = "Names";
    private const string DestsKey = "Dests";

    /// <summary>
    /// Строит тело словаря <c>/Names</c>: <c>/Dests</c> = <paramref name="destsRef"/>, прочие подключи
    /// скопированы из <paramref name="existingNames"/> (если он был). Используется, когда у catalog'а
    /// уже есть <c>/Names</c> с другими ветками name-tree, которые нужно сохранить.
    /// </summary>
    public static string WriteNamesDict(DictionaryToken? existingNames, IndirectReference destsRef)
    {
        var sb = new StringBuilder("<<\n");
        if (existingNames is not null)
        {
            foreach (var kv in existingNames.Data)
            {
                if (string.Equals(kv.Key, DestsKey, StringComparison.Ordinal))
                {
                    continue; // переписываем ниже
                }

                sb.Append('/').Append(kv.Key).Append(' ');
                PdfDictionaryCosWriter.WriteAnyToken(sb, kv.Value);
                sb.Append('\n');
            }
        }

        sb.Append(CultureInfo.InvariantCulture,
            $"/Dests {destsRef.ObjectNumber} {destsRef.Generation} R\n");
        sb.Append(">>");
        return sb.ToString();
    }

    /// <summary>
    /// Копирует <paramref name="catalog"/>, перезаписывая <c>/Names</c> ссылкой
    /// <paramref name="namesRef"/> на (обновлённый) словарь имён. Все прочие ключи копируются как есть.
    /// </summary>
    public static string WriteCatalogWithNames(DictionaryToken catalog, IndirectReference namesRef)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var sb = new StringBuilder("<<\n");
        foreach (var kv in catalog.Data)
        {
            if (string.Equals(kv.Key, NamesKey, StringComparison.Ordinal))
            {
                continue; // переписываем ниже
            }

            sb.Append('/').Append(kv.Key).Append(' ');
            PdfDictionaryCosWriter.WriteAnyToken(sb, kv.Value);
            sb.Append('\n');
        }

        sb.Append(CultureInfo.InvariantCulture, $"/Names {namesRef.ObjectNumber} {namesRef.Generation} R\n");
        sb.Append(">>");
        return sb.ToString();
    }
}
