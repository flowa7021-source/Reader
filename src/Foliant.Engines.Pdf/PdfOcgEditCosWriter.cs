using System.Text;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf;

/// <summary>
/// cos-writer для rename / delete одного OCG (Optional Content Group, «PDF layer»), дополняющий
/// toggle-writer <see cref="PdfOcgCosWriter"/> (PDF spec §8.11). Работает поверх снимка
/// <see cref="PdfOcgCosReader"/> и дописывает обновлённые объекты инкрементальным апдейтом
/// (<see cref="PdfIncrementalWriter"/>); исходные байты не мутируются (ISO 32000-1 §7.5.6).
///
/// <list type="bullet">
/// <item><b>Rename</b> переписывает только сам OCG-объект (тот же object number) — копирует его
/// словарь через <see cref="PdfDictionaryCosWriter.WriteAnyToken"/>, подменяя <c>/Name</c> на
/// новый <see cref="PdfTextString"/>. Catalog / <c>/OCProperties</c> при этом не трогаются.</item>
/// <item><b>Remove</b> переписывает <c>/OCProperties</c>: целевой ref убирается из <c>/OCGs</c>,
/// а из default-config <c>/D</c> — из <c>/ON</c>/<c>/OFF</c>/<c>/Order</c>. Сам OCG-объект
/// остаётся в файле (на него больше нет ссылок — становится недостижимым), content-stream'ы
/// не трогаются: контент с маркером удалённого слоя становится всегда видимым.</item>
/// </list>
///
/// <para>Резолв <c>/OCProperties</c> зеркалит <see cref="PdfiumOcgService"/>: если он индирект —
/// переписываем индирект-объект; если inline в catalog'е — переписываем catalog
/// (<see cref="PdfCatalogCosWriter"/>). Вложенный <c>/D</c> нормализуется в inline (как и в
/// toggle-writer'е): PDF spec не требует <c>/D</c> быть индиректом.</para>
/// </summary>
internal static class PdfOcgEditCosWriter
{
    private const string NameKey = "Name";
    private const string OCGsKey = "OCGs";
    private const string DKey = "D";
    private const string OnKey = "ON";
    private const string OffKey = "OFF";
    private const string OrderKey = "Order";

    /// <summary>Переименовывает OCG с 0-based <paramref name="index"/> (индекс в <c>/OCGs</c>),
    /// возвращает байты нового PDF. <paramref name="source"/> не мутируется.</summary>
    /// <param name="source">Исходные байты PDF.</param>
    /// <param name="index">0-based индекс слоя в массиве <c>/OCProperties → /OCGs</c>.</param>
    /// <param name="newName">Новое имя; пишется как PDF text-string (<see cref="PdfTextString"/>).</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> или
    /// <paramref name="newName"/> равны <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> вне диапазона
    /// <c>[0, OcgRefs.Count)</c>.</exception>
    public static byte[] Rename(byte[] source, int index, string newName)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(newName);

        var snapshot = PdfOcgCosReader.Read(source);
        ValidateIndex(snapshot, index);

        var targetRef = snapshot.OcgRefs[index];
        string ocgBody = WriteOcgWithName(source, targetRef, newName);

        long prevXref = PdfIncrementalWriter.FindLastStartXref(source);
        var updated = new[] { new PdfIncrementalWriter.RawObject(targetRef, ocgBody) };
        return PdfIncrementalWriter.Append(
            source, [], updated, prevXref, snapshot.RootObjectNumber, snapshot.TrailerSize);
    }

    /// <summary>Удаляет OCG с 0-based <paramref name="index"/> из <c>/OCGs</c> и из <c>/D</c>
    /// (<c>/ON</c>/<c>/OFF</c>/<c>/Order</c>), возвращает байты нового PDF.
    /// <paramref name="source"/> не мутируется.</summary>
    /// <param name="source">Исходные байты PDF.</param>
    /// <param name="index">0-based индекс слоя в массиве <c>/OCProperties → /OCGs</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> равен
    /// <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> вне диапазона
    /// <c>[0, OcgRefs.Count)</c>.</exception>
    /// <exception cref="InvalidDataException"><c>/OCProperties</c> отсутствует (рассинхрон
    /// со снимком, не должно случаться при валидном index).</exception>
    public static byte[] Remove(byte[] source, int index)
    {
        ArgumentNullException.ThrowIfNull(source);

        var snapshot = PdfOcgCosReader.Read(source);
        ValidateIndex(snapshot, index);

        if (snapshot.OCPropertiesDict is null)
        {
            throw new InvalidDataException("PDF has no /OCProperties to remove a layer from.");
        }

        var targetRef = snapshot.OcgRefs[index];
        string ocPropertiesBody = WriteOcPropertiesWithoutRef(snapshot.OCPropertiesDict, targetRef);

        long prevXref = PdfIncrementalWriter.FindLastStartXref(source);
        long rootObj = snapshot.RootObjectNumber;
        long trailerSize = snapshot.TrailerSize;

        if (snapshot.OCPropertiesRef is { } ocpRef)
        {
            // /OCProperties — индирект: переписываем сам объект.
            var updated = new[] { new PdfIncrementalWriter.RawObject(ocpRef, ocPropertiesBody) };
            return PdfIncrementalWriter.Append(source, [], updated, prevXref, rootObj, trailerSize);
        }

        // /OCProperties inline в catalog'е → переписываем catalog (/Root в трейлере).
        string catalogBody = PdfCatalogCosWriter.WriteCatalogWithInlineOCProperties(snapshot, ocPropertiesBody);
        var updatedCatalog = new[]
        {
            new PdfIncrementalWriter.RawObject(new IndirectReference(rootObj, 0), catalogBody),
        };
        return PdfIncrementalWriter.Append(source, [], updatedCatalog, prevXref, rootObj, trailerSize);
    }

    private static void ValidateIndex(PdfOcgCosReader.OcgSnapshot snapshot, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, snapshot.OcgRefs.Count);
    }

    private static string WriteOcgWithName(byte[] source, IndirectReference targetRef, string newName)
    {
        using var doc = PdfPigDocument.Open(source);
        if (doc.Structure.GetObject(targetRef) is not ObjectToken { Data: DictionaryToken ocgDict })
        {
            // Снимок отдал ref, но объект не разрезолвился — выписываем минимальный валидный OCG.
            var fallback = new StringBuilder("<< /Type /OCG /Name ");
            PdfTextString.Append(fallback, newName);
            fallback.Append(" >>");
            return fallback.ToString();
        }

        var sb = new StringBuilder("<<\n");
        foreach (var kv in ocgDict.Data)
        {
            if (string.Equals(kv.Key, NameKey, StringComparison.Ordinal))
            {
                continue; // переписываем ниже
            }

            sb.Append('/').Append(kv.Key).Append(' ');
            PdfDictionaryCosWriter.WriteAnyToken(sb, kv.Value);
            sb.Append('\n');
        }

        sb.Append("/Name ");
        PdfTextString.Append(sb, newName);
        sb.Append('\n');
        sb.Append(">>");
        return sb.ToString();
    }

    private static string WriteOcPropertiesWithoutRef(DictionaryToken ocPropertiesDict, IndirectReference targetRef)
    {
        var sb = new StringBuilder("<<\n");
        DictionaryToken? originalD = ExtractInlineD(ocPropertiesDict);
        foreach (var kv in ocPropertiesDict.Data)
        {
            if (string.Equals(kv.Key, OCGsKey, StringComparison.Ordinal))
            {
                sb.Append("/OCGs ");
                WriteFilteredRefArray(sb, kv.Value, targetRef);
                sb.Append('\n');
                continue;
            }

            if (string.Equals(kv.Key, DKey, StringComparison.Ordinal))
            {
                continue; // переписываем ниже (нормализуем в inline)
            }

            sb.Append('/').Append(kv.Key).Append(' ');
            PdfDictionaryCosWriter.WriteAnyToken(sb, kv.Value);
            sb.Append('\n');
        }

        if (originalD is not null)
        {
            sb.Append("/D ");
            WriteDWithoutRef(sb, originalD, targetRef);
            sb.Append('\n');
        }

        sb.Append(">>");
        return sb.ToString();
    }

    private static DictionaryToken? ExtractInlineD(DictionaryToken ocPropertiesDict)
    {
        // Зеркалит PdfOcgCosWriter.ExtractOriginalD: индирект /D PdfPig здесь не резолвит без
        // TokenScanner; нормализуем в inline (PDF spec не требует /D быть индиректом).
        if (ocPropertiesDict.TryGet(NameToken.Create(DKey), out IToken? raw) && raw is DictionaryToken inline)
        {
            return inline;
        }

        return null;
    }

    private static void WriteDWithoutRef(StringBuilder sb, DictionaryToken dDict, IndirectReference targetRef)
    {
        sb.Append("<<\n");
        foreach (var kv in dDict.Data)
        {
            if (IsRefArrayKey(kv.Key))
            {
                sb.Append('/').Append(kv.Key).Append(' ');
                WriteFilteredRefArray(sb, kv.Value, targetRef);
                sb.Append('\n');
                continue;
            }

            sb.Append('/').Append(kv.Key).Append(' ');
            PdfDictionaryCosWriter.WriteAnyToken(sb, kv.Value);
            sb.Append('\n');
        }

        sb.Append(">>");
    }

    private static bool IsRefArrayKey(string key) =>
        string.Equals(key, OnKey, StringComparison.Ordinal)
        || string.Equals(key, OffKey, StringComparison.Ordinal)
        || string.Equals(key, OrderKey, StringComparison.Ordinal);

    private static void WriteFilteredRefArray(StringBuilder sb, IToken arrayToken, IndirectReference targetRef)
    {
        // /Order может содержать вложенные массивы (групповые заголовки) и refs вперемешку —
        // фильтруем рекурсивно, выкидывая только сам targetRef, сохраняя структуру.
        if (arrayToken is not ArrayToken arr)
        {
            // Не массив (например, /Order индиректом) — копируем как есть; ссылку из него
            // не выкидываем, но недостижимый OCG безвреден (всегда-видим, как и задокументировано).
            PdfDictionaryCosWriter.WriteAnyToken(sb, arrayToken);
            return;
        }

        sb.Append('[');
        bool first = true;
        foreach (var item in arr.Data)
        {
            if (item is IndirectReferenceToken iref && iref.Data.Equals(targetRef))
            {
                continue; // выкидываем целевой ref
            }

            if (!first)
            {
                sb.Append(' ');
            }

            first = false;

            if (item is ArrayToken nested)
            {
                WriteFilteredRefArray(sb, nested, targetRef);
            }
            else
            {
                PdfDictionaryCosWriter.WriteAnyToken(sb, item);
            }
        }

        sb.Append(']');
    }
}
