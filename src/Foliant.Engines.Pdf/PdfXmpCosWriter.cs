using System.Globalization;
using System.Text;
using UglyToad.PdfPig.Core;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Записывает document-level XMP-пакет в исходные байты PDF инкрементальным апдейтом (симметрично
/// <see cref="PdfXmpCosReader"/>'у). Эмитит один новый metadata-поток
/// <c>&lt;&lt; /Type /Metadata /Subtype /XML /Length N &gt;&gt;</c> с несжатым телом (UTF-8-байты
/// пакета) и переписывает catalog, проставляя <c>/Metadata N 0 R</c> (через
/// <see cref="PdfCatalogMetadataCosWriter"/>). Существующий <c>/Metadata</c> заменяется новым.
/// Оригинальные байты не меняются (ISO 32000-1 §7.5.6, §14.3.2).
/// </summary>
internal static class PdfXmpCosWriter
{
    /// <summary>
    /// Создаёт / заменяет <c>/Metadata</c>-поток байтами <paramref name="xmpPacket"/> (UTF-8, без
    /// сжатия: <c>/Length</c> = число UTF-8-байт, без <c>/Filter</c>). Пустой пакет допустим (поток с
    /// нулевой длиной). Возвращает байты нового PDF; <paramref name="source"/> не мутируется.
    /// </summary>
    public static byte[] Write(byte[] source, string xmpPacket)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(xmpPacket);

        using var doc = PdfPigDocument.Open(source);
        var trailer = doc.Structure.CrossReferenceTable.Trailer;
        var catalog = doc.Structure.Catalog.CatalogDictionary;
        long nextObj = MaxObjectNumber(doc) + 1;

        byte[] payload = Encoding.UTF8.GetBytes(xmpPacket);
        var metadataRef = new IndirectReference(nextObj++, 0);
        var newObjects = new[]
        {
            new PdfIncrementalWriter.RawObject(metadataRef, WriteMetadataStream(payload)),
        };

        string catalogBody = PdfCatalogMetadataCosWriter.WriteCatalogWithMetadata(catalog, metadataRef);
        var updated = new[] { new PdfIncrementalWriter.RawObject(trailer.Root, catalogBody) };

        long prevXref = PdfIncrementalWriter.FindLastStartXref(source);
        return PdfIncrementalWriter.Append(
            source, newObjects, updated, prevXref, trailer.Root.ObjectNumber, trailer.Size);
    }

    private static string WriteMetadataStream(byte[] payload)
    {
        // Несжатый metadata-поток: /Type /Metadata /Subtype /XML, /Length = число UTF-8-байт (НЕ
        // символов), без /Filter. Тело выписываем как Latin1-строку (1:1 байт↔char для 0–255), т.к.
        // PdfIncrementalWriter re-кодирует тело объекта через Encoding.Latin1 — это byte-preserving
        // для всех байт 0x00–0xFF (тот же приём, что в PdfAttachmentCosWriter).
        var sb = new StringBuilder(payload.Length + 96);
        sb.Append("<<\n/Type /Metadata\n/Subtype /XML\n");
        sb.Append(CultureInfo.InvariantCulture, $"/Length {payload.Length}\n");
        sb.Append(">>\nstream\n");
        sb.Append(Encoding.Latin1.GetString(payload));
        sb.Append("\nendstream");
        return sb.ToString();
    }

    private static long MaxObjectNumber(PdfPigDocument doc)
    {
        long max = 0;
        foreach (var key in doc.Structure.CrossReferenceTable.ObjectOffsets.Keys)
        {
            if (key.ObjectNumber > max)
            {
                max = key.ObjectNumber;
            }
        }

        return max;
    }
}
