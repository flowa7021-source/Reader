using System.Globalization;
using System.Text;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Сериализатор обновлённого <c>/OCProperties</c>-словаря для инкрементального апдейта PDF
/// (Q-F8). Принимает оригинальный словарь и желаемые <c>/OFF</c>/<c>/ON</c> наборы (refs
/// слоёв, которые должны быть скрыты / показаны явно), и выписывает cos-text словарь, в
/// котором <c>/D</c> подменён на новый — c обновлённым <c>BaseState=ON</c> и
/// перевыписанными <c>/ON</c>/<c>/OFF</c> массивами. Остальные ключи копируются как есть.
///
/// <para>Поведение по умолчанию: BaseState='ON' (все слои видимы), <c>/OFF</c> — explicit
/// hidden, <c>/ON</c> — explicit visible (нужно только если <c>BaseState=OFF</c> или у
/// /D есть <c>/AS</c>-overrides). MVP не трогает <c>/Order</c>, <c>/RBGroups</c>,
/// <c>/Locked</c>, <c>/AS</c> — они проходят сквозь без изменений.</para>
/// </summary>
internal static class PdfOcgCosWriter
{
    private const string DKey = "D";
    private const string OnKey = "ON";
    private const string OffKey = "OFF";
    private const string BaseStateKey = "BaseState";

    /// <summary>Сериализует <paramref name="ocPropertiesDict"/> в cos-словарь, заменив /D
    /// (или создав, если его не было) на dict с указанными <paramref name="explicitOff"/>
    /// и <paramref name="explicitOn"/> наборами.</summary>
    public static string WriteOcPropertiesWithVisibility(
        DictionaryToken ocPropertiesDict,
        IReadOnlyList<IndirectReference> explicitOff,
        IReadOnlyList<IndirectReference> explicitOn)
    {
        ArgumentNullException.ThrowIfNull(ocPropertiesDict);
        ArgumentNullException.ThrowIfNull(explicitOff);
        ArgumentNullException.ThrowIfNull(explicitOn);

        var sb = new StringBuilder("<<\n");
        DictionaryToken? originalD = ExtractOriginalD(ocPropertiesDict);
        foreach (var kv in ocPropertiesDict.Data)
        {
            if (string.Equals(kv.Key, DKey, StringComparison.Ordinal))
            {
                continue; // переписываем ниже
            }

            sb.Append('/').Append(kv.Key).Append(' ');
            PdfDictionaryCosWriter.WriteAnyToken(sb, kv.Value);
            sb.Append('\n');
        }

        sb.Append("/D ");
        WriteDDict(sb, originalD, explicitOff, explicitOn);
        sb.Append('\n');
        sb.Append(">>");
        return sb.ToString();
    }

    private static DictionaryToken? ExtractOriginalD(DictionaryToken ocPropertiesDict)
    {
        if (ocPropertiesDict.TryGet(NameToken.Create(DKey), out IToken? raw)
            && raw is DictionaryToken inline)
        {
            return inline;
        }

        // Если /D живёт в indirect-объекте, parser PdfPig его не резолвит без TokenScanner.
        // Reader при этом отдаёт нам валидный dict через ResolveDefaultConfig, но writer
        // переписывает ту же родительскую структуру с inline /D — это допустимая
        // нормализация (PDF spec не требует /D быть indirect; Acrobat читает и так, и так).
        return null;
    }

    private static void WriteDDict(
        StringBuilder sb,
        DictionaryToken? originalD,
        IReadOnlyList<IndirectReference> explicitOff,
        IReadOnlyList<IndirectReference> explicitOn)
    {
        sb.Append("<<\n");
        bool baseWritten = false;
        if (originalD is not null)
        {
            foreach (var kv in originalD.Data)
            {
                if (IsOcgVisibilityKey(kv.Key))
                {
                    if (string.Equals(kv.Key, BaseStateKey, StringComparison.Ordinal))
                    {
                        baseWritten = true;
                        sb.Append("/BaseState /ON\n");
                    }

                    continue;
                }

                sb.Append('/').Append(kv.Key).Append(' ');
                PdfDictionaryCosWriter.WriteAnyToken(sb, kv.Value);
                sb.Append('\n');
            }
        }

        if (!baseWritten)
        {
            sb.Append("/BaseState /ON\n");
        }

        if (explicitOn.Count > 0)
        {
            sb.Append("/ON ");
            WriteRefArray(sb, explicitOn);
            sb.Append('\n');
        }

        if (explicitOff.Count > 0)
        {
            sb.Append("/OFF ");
            WriteRefArray(sb, explicitOff);
            sb.Append('\n');
        }

        sb.Append(">>");
    }

    private static bool IsOcgVisibilityKey(string key) =>
        string.Equals(key, OnKey, StringComparison.Ordinal)
        || string.Equals(key, OffKey, StringComparison.Ordinal)
        || string.Equals(key, BaseStateKey, StringComparison.Ordinal);

    private static void WriteRefArray(StringBuilder sb, IReadOnlyList<IndirectReference> refs)
    {
        sb.Append('[');
        for (int i = 0; i < refs.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            sb.Append(CultureInfo.InvariantCulture,
                $"{refs[i].ObjectNumber} {refs[i].Generation} R");
        }

        sb.Append(']');
    }
}
