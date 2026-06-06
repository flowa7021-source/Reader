using System.Globalization;
using System.Text;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Переписывает catalog (/Root) словарь и его подсловарь <c>/Names</c> при работе с встроенными
/// вложениями: <c>/EmbeddedFiles</c> внутри <c>/Names</c> заменяется ссылкой на новый name-tree, а
/// прочие подключи (<c>/Dests</c>, <c>/JavaScript</c>, …) и прочие ключи catalog'а копируются как есть
/// через <see cref="PdfDictionaryCosWriter.WriteAnyToken"/>. Зеркально
/// <see cref="PdfCatalogPageLabelCosWriter"/>'у — отдельный writer, т.к. ключ (<c>/Names</c>) и
/// семантика (вложенный name-tree) другие.
/// </summary>
internal static class PdfCatalogNamesCosWriter
{
    private const string NamesKey = "Names";
    private const string EmbeddedFilesKey = "EmbeddedFiles";

    /// <summary>
    /// Строит тело словаря <c>/Names</c>: <c>/EmbeddedFiles</c> = <paramref name="embeddedFilesRef"/>,
    /// прочие подключи скопированы из <paramref name="existingNames"/> (если он был). Используется,
    /// когда у catalog'а уже есть <c>/Names</c> с другими ветками name-tree, которые нужно сохранить.
    /// </summary>
    public static string WriteNamesDict(DictionaryToken? existingNames, IndirectReference embeddedFilesRef)
    {
        var sb = new StringBuilder("<<\n");
        if (existingNames is not null)
        {
            foreach (var kv in existingNames.Data)
            {
                if (string.Equals(kv.Key, EmbeddedFilesKey, StringComparison.Ordinal))
                {
                    continue; // переписываем ниже
                }

                sb.Append('/').Append(kv.Key).Append(' ');
                PdfDictionaryCosWriter.WriteAnyToken(sb, kv.Value);
                sb.Append('\n');
            }
        }

        sb.Append(CultureInfo.InvariantCulture,
            $"/EmbeddedFiles {embeddedFilesRef.ObjectNumber} {embeddedFilesRef.Generation} R\n");
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
