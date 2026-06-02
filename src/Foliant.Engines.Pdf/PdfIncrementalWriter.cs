using System.Globalization;
using System.Text;
using UglyToad.PdfPig.Core;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Минимальный writer инкрементального апдейта PDF (ISO 32000-1 §7.5.6). Используется
/// <see cref="PdfPigAnnotationAppender"/>'ом: дописывает новые/обновлённые объекты в конец
/// оригинальных байт, прикрепляет xref-секцию + trailer с <c>/Prev</c> на старую xref.
///
/// Гарантирует: оригинальные байты <b>не</b> изменяются (важно для подписей и для верификации
/// FRAGILE кейсов вроде incremental signed PDF), новый PDF читается всеми стандартными reader'ами,
/// объекты дописываются в порядке вызова — это важно для воспроизводимости тестов.
/// </summary>
internal static class PdfIncrementalWriter
{
    /// <summary>
    /// Дописывает к <paramref name="original"/> новые объекты <paramref name="newObjects"/> +
    /// обновлённые версии существующих объектов <paramref name="updatedObjects"/> +
    /// xref/trailer. Возвращает байты нового PDF.
    /// </summary>
    /// <param name="original">PDFium-сгенерированные байты PDF.</param>
    /// <param name="newObjects">Список (IndirectReference, cos-body) для добавления; reference'ы
    /// должны быть свежими (objectNumber > max существующего).</param>
    /// <param name="updatedObjects">Список (IndirectReference, cos-body) для замены существующих
    /// объектов; reference'ы должны совпадать с объектами в оригинале.</param>
    /// <param name="prevXrefOffset">Офсет самой свежей xref в <paramref name="original"/>
    /// (из последнего <c>startxref</c>).</param>
    /// <param name="trailerRootObj">Object number катога /Root (берётся из старого trailer'а
    /// и копируется в новый, чтобы reader дошёл до /Catalog).</param>
    /// <param name="oldSize">Старое /Size (для /Size = max(old, new max+1)).</param>
    public static byte[] Append(
        byte[] original,
        IReadOnlyList<RawObject> newObjects,
        IReadOnlyList<RawObject> updatedObjects,
        long prevXrefOffset,
        long trailerRootObj,
        long oldSize)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(newObjects);
        ArgumentNullException.ThrowIfNull(updatedObjects);

        using var ms = new MemoryStream(original.Length + 4096);
        ms.Write(original, 0, original.Length);
        EnsureTrailingNewline(ms);

        var entries = new List<(IndirectReference Ref, long Offset)>(newObjects.Count + updatedObjects.Count);
        foreach (var obj in newObjects)
        {
            entries.Add((obj.Reference, WriteObject(ms, obj)));
        }

        foreach (var obj in updatedObjects)
        {
            entries.Add((obj.Reference, WriteObject(ms, obj)));
        }

        long xrefOffset = ms.Position;
        WriteXrefAndTrailer(ms, entries, prevXrefOffset, trailerRootObj, oldSize);
        WriteAscii(ms, string.Create(CultureInfo.InvariantCulture, $"startxref\n{xrefOffset}\n%%EOF\n"));
        return ms.ToArray();
    }

    /// <summary>Находит офсет последней <c>startxref</c> в файле (самая свежая xref-таблица).</summary>
    public static long FindLastStartXref(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ReadOnlySpan<byte> span = pdf;
        int idx = span.LastIndexOf("startxref"u8);
        if (idx < 0)
        {
            throw new InvalidDataException("PDF is missing startxref marker.");
        }

        int p = idx + "startxref".Length;
        while (p < pdf.Length && IsWhitespace(pdf[p]))
        {
            p++;
        }

        int digitStart = p;
        while (p < pdf.Length && pdf[p] >= (byte)'0' && pdf[p] <= (byte)'9')
        {
            p++;
        }

        if (p == digitStart)
        {
            throw new InvalidDataException("PDF startxref offset is missing.");
        }

        return long.Parse(Encoding.Latin1.GetString(pdf, digitStart, p - digitStart), CultureInfo.InvariantCulture);
    }

    private static long WriteObject(MemoryStream ms, RawObject obj)
    {
        long offset = ms.Position;
        string header = string.Create(CultureInfo.InvariantCulture, $"{obj.Reference.ObjectNumber} {obj.Reference.Generation} obj\n");
        WriteAscii(ms, header);
        WriteAscii(ms, obj.Body);
        WriteAscii(ms, "\nendobj\n");
        return offset;
    }

    private static void WriteXrefAndTrailer(
        MemoryStream ms,
        List<(IndirectReference Ref, long Offset)> entries,
        long prevXrefOffset,
        long trailerRootObj,
        long oldSize)
    {
        entries.Sort(static (a, b) => a.Ref.ObjectNumber.CompareTo(b.Ref.ObjectNumber));
        var sb = new StringBuilder(256 + (entries.Count * 32));
        sb.Append("xref\n");
        AppendXrefSubsections(sb, entries);

        long newSize = Math.Max(oldSize, MaxObjectNumberPlusOne(entries));
        sb.Append("trailer\n");
        sb.Append(CultureInfo.InvariantCulture, $"<< /Size {newSize} /Prev {prevXrefOffset} /Root {trailerRootObj} 0 R >>\n");
        WriteAscii(ms, sb.ToString());
    }

    private static void AppendXrefSubsections(StringBuilder sb, List<(IndirectReference Ref, long Offset)> entries)
    {
        // Группируем подряд идущие object numbers в subsection'ы — каноничная xref-форма.
        int i = 0;
        while (i < entries.Count)
        {
            int j = i + 1;
            while (j < entries.Count && entries[j].Ref.ObjectNumber == entries[j - 1].Ref.ObjectNumber + 1)
            {
                j++;
            }

            long first = entries[i].Ref.ObjectNumber;
            int count = j - i;
            sb.Append(CultureInfo.InvariantCulture, $"{first} {count}\n");
            for (int k = i; k < j; k++)
            {
                // n-entry: 10-digit offset, 5-digit generation, "n", trailing space+LF (20 bytes total).
                sb.Append(CultureInfo.InvariantCulture,
                    $"{entries[k].Offset:D10} {entries[k].Ref.Generation:D5} n \n");
            }

            i = j;
        }
    }

    private static long MaxObjectNumberPlusOne(List<(IndirectReference Ref, long)> entries)
    {
        long max = 0;
        foreach (var (r, _) in entries)
        {
            if (r.ObjectNumber > max)
            {
                max = r.ObjectNumber;
            }
        }

        return max + 1;
    }

    private static void EnsureTrailingNewline(MemoryStream ms)
    {
        if (ms.Length == 0)
        {
            return;
        }

        ms.Position = ms.Length - 1;
        int last = ms.ReadByte();
        ms.Position = ms.Length;
        if (last != '\n')
        {
            ms.WriteByte((byte)'\n');
        }
    }

    private static void WriteAscii(MemoryStream ms, string s)
    {
        byte[] bytes = Encoding.Latin1.GetBytes(s);
        ms.Write(bytes, 0, bytes.Length);
    }

    private static bool IsWhitespace(byte b) =>
        b is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t' or (byte)'\f' or 0;

    /// <summary>Сериализованный PDF cos-объект: <see cref="Reference"/> определяет (object number,
    /// generation), <see cref="Body"/> — тело объекта между <c>obj</c>/<c>endobj</c> маркерами.</summary>
    public sealed record RawObject(IndirectReference Reference, string Body);
}
