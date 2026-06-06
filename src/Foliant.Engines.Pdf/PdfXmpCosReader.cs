using System.IO.Compression;
using System.Text;
using UglyToad.PdfPig.Tokens;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Читает document-level XMP-пакет из cos-структуры PDF: catalog-ключ <c>/Metadata</c> разрешается в
/// metadata-поток (ISO 32000-1 §14.3.2), его байты декодируются (несжатый поток — как есть,
/// <c>/FlateDecode</c> — инфляция через zlib) и трактуются как UTF-8 XML. Симметрично
/// <see cref="PdfXmpCosWriter"/>'у. Доступ к <see cref="StreamToken"/> — тем же приёмом, что и в
/// <see cref="PdfAttachmentCosReader"/>.
/// </summary>
internal static class PdfXmpCosReader
{
    private static readonly NameToken MetadataName = NameToken.Create("Metadata");
    private static readonly NameToken FilterName = NameToken.Create("Filter");
    private static readonly NameToken FlateDecodeName = NameToken.Create("FlateDecode");

    /// <summary>Читает XMP-пакет документа как UTF-8-строку. Нет <c>/Metadata</c> (или ключ не
    /// разрешается в поток) → <see langword="null"/>. Ведущий UTF-8 BOM в декодированных байтах
    /// отбрасывается.</summary>
    public static string? Read(byte[] pdfBytes)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);

        using var doc = PdfPigDocument.Open(pdfBytes);
        var catalog = doc.Structure.Catalog.CatalogDictionary;
        if (!TryResolveStream(doc, catalog, out var stream))
        {
            return null;
        }

        byte[] decoded = DecodeStream(stream.StreamDictionary, stream.Data);
        return DecodeUtf8(decoded);
    }

    private static bool TryResolveStream(PdfPigDocument doc, DictionaryToken catalog, out StreamToken stream)
    {
        stream = null!;
        if (!catalog.TryGet(MetadataName, out IToken? raw) || raw is null)
        {
            return false;
        }

        switch (raw)
        {
            case StreamToken inline:
                // Inline-поток в catalog'е маловероятен, но обрабатываем на всякий случай.
                stream = inline;
                return true;
            case IndirectReferenceToken iref
                when doc.Structure.GetObject(iref.Data) is ObjectToken { Data: StreamToken resolved }:
                stream = resolved;
                return true;
            default:
                return false;
        }
    }

    private static byte[] DecodeStream(DictionaryToken streamDict, ReadOnlyMemory<byte> raw)
    {
        if (!TryGetSingleFilter(streamDict, out string? filter))
        {
            // Нет /Filter — байты потока хранятся как есть (по соглашению metadata-поток несжат).
            return raw.ToArray();
        }

        if (string.Equals(filter, FlateDecodeName.Data, StringComparison.Ordinal))
        {
            return Inflate(raw);
        }

        throw new NotSupportedException(
            $"Metadata stream uses unsupported filter '{filter}'; only FlateDecode (or none) is supported.");
    }

    private static bool TryGetSingleFilter(DictionaryToken streamDict, out string? filter)
    {
        filter = null;
        if (!streamDict.TryGet(FilterName, out IToken? raw) || raw is null)
        {
            return false;
        }

        switch (raw)
        {
            case NameToken name:
                filter = name.Data;
                return true;
            case ArrayToken { Length: 1 } arr when arr.Data[0] is NameToken only:
                filter = only.Data;
                return true;
            case ArrayToken { Length: 0 }:
                return false; // пустой массив фильтров == без фильтра
            default:
                throw new NotSupportedException(
                    "Metadata stream uses a multi-filter chain; only a single FlateDecode (or none) is supported.");
        }
    }

    private static byte[] Inflate(ReadOnlyMemory<byte> compressed)
    {
        using var input = new MemoryStream(compressed.ToArray(), writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private static string DecodeUtf8(byte[] bytes)
    {
        // Отбрасываем ведущий UTF-8 BOM (EF BB BF), если он есть, чтобы вернуть «чистый» XMP-пакет.
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        return Encoding.UTF8.GetString(bytes);
    }
}
