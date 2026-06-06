using System.Text;
using UglyToad.PdfPig.Tokens;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Переписывает catalog (/Root) словарь, удаляя document-level скрипты / действия: JavaScript-
/// <c>/OpenAction</c>, ветку <c>/Names → /JavaScript</c> и <c>/AA</c>. Безопасный (GoTo /
/// destination-array) <c>/OpenAction</c> и прочие подключи <c>/Names</c> (<c>/EmbeddedFiles</c>,
/// <c>/Dests</c>, …) сохраняются; всё остальное копируется как есть через
/// <see cref="PdfDictionaryCosWriter.WriteAnyToken"/>. Зеркально <see cref="PdfCatalogNamesCosWriter"/>'у —
/// отдельный writer, т.к. набор удаляемых ключей и логика сохранения подключей <c>/Names</c> другие.
/// </summary>
internal static class PdfCatalogSanitizationCosWriter
{
    private const string OpenActionKey = "OpenAction";
    private const string NamesKey = "Names";
    private const string JavaScriptKey = "JavaScript";
    private const string AaKey = "AA";

    /// <summary>
    /// Копирует <paramref name="catalog"/> без document-level скриптов / действий.
    /// <paramref name="dropOpenAction"/> — опустить <c>/OpenAction</c> (вызывающий уже проверил, что
    /// это JavaScript). <paramref name="dropAa"/> — опустить <c>/AA</c>. <c>/Names</c>
    /// (<paramref name="existingNames"/>) переписывается без <c>/JavaScript</c>: если других подключей
    /// не остаётся, ключ <c>/Names</c> опускается целиком, иначе пишется как inline-словарь с
    /// сохранёнными подключами.
    /// </summary>
    /// <param name="catalog">Исходный catalog-словарь.</param>
    /// <param name="dropOpenAction">Опустить <c>/OpenAction</c> (JavaScript open-action).</param>
    /// <param name="dropAa">Опустить <c>/AA</c> (document additional-actions).</param>
    /// <param name="existingNames">Разрешённый словарь <c>/Names</c> catalog'а или
    /// <see langword="null"/>, если его нет.</param>
    /// <returns>Тело переписанного catalog-словаря (между <c>obj</c>/<c>endobj</c>).</returns>
    public static string WriteSanitizedCatalog(
        DictionaryToken catalog, bool dropOpenAction, bool dropAa, DictionaryToken? existingNames)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var sb = new StringBuilder("<<\n");
        foreach (var kv in catalog.Data)
        {
            if (ShouldDropCatalogKey(kv.Key, dropOpenAction, dropAa))
            {
                continue; // удаляем целиком
            }

            if (string.Equals(kv.Key, NamesKey, StringComparison.Ordinal))
            {
                continue; // переписываем (или опускаем) ниже
            }

            sb.Append('/').Append(kv.Key).Append(' ');
            PdfDictionaryCosWriter.WriteAnyToken(sb, kv.Value);
            sb.Append('\n');
        }

        AppendSanitizedNames(sb, existingNames);
        sb.Append(">>");
        return sb.ToString();
    }

    private static bool ShouldDropCatalogKey(string key, bool dropOpenAction, bool dropAa)
    {
        if (dropOpenAction && string.Equals(key, OpenActionKey, StringComparison.Ordinal))
        {
            return true;
        }

        return dropAa && string.Equals(key, AaKey, StringComparison.Ordinal);
    }

    private static void AppendSanitizedNames(StringBuilder sb, DictionaryToken? existingNames)
    {
        if (existingNames is null)
        {
            return; // у catalog'а не было /Names — ничего не пишем
        }

        // Сохраняем все подключи /Names, кроме /JavaScript. Если ничего не осталось — /Names опускаем
        // целиком (мёртвый пустой словарь не нужен).
        bool wroteAny = false;
        var inner = new StringBuilder("<<\n");
        foreach (var kv in existingNames.Data)
        {
            if (string.Equals(kv.Key, JavaScriptKey, StringComparison.Ordinal))
            {
                continue; // удаляем ветку document-level JavaScript
            }

            inner.Append('/').Append(kv.Key).Append(' ');
            PdfDictionaryCosWriter.WriteAnyToken(inner, kv.Value);
            inner.Append('\n');
            wroteAny = true;
        }

        if (!wroteAny)
        {
            return; // /Names содержал только /JavaScript — опускаем /Names
        }

        inner.Append(">>");
        sb.Append("/Names ").Append(inner).Append('\n');
    }
}
