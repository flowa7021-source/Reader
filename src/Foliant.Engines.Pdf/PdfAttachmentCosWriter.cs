using System.Globalization;
using System.Text;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Добавляет / удаляет встроенные вложения, переписывая catalog name-tree
/// <c>/Names → /EmbeddedFiles</c> инкрементальным апдейтом (симметрично
/// <see cref="PdfAttachmentCosReader"/>'у). Существующие вложения (их file-spec / stream объекты)
/// сохраняются — в плоский <c>/Names</c>-массив заново выписываются их ссылки + новая запись.
/// Оригинальные байты не меняются (ISO 32000-1 §7.5.6).
/// </summary>
internal static class PdfAttachmentCosWriter
{
    private static readonly NameToken NamesName = NameToken.Create("Names");

    /// <summary>
    /// Встраивает <paramref name="fileBytes"/> под именем <paramref name="fileName"/> (без сжатия:
    /// <c>/Length</c> = длина, <c>/Params &lt;&lt; /Size N &gt;&gt;</c>, без <c>/Filter</c>).
    /// Существующие вложения сохраняются; коллизия по имени → старая запись заменяется новой.
    /// Возвращает байты нового PDF; <paramref name="source"/> не мутируется.
    /// </summary>
    public static byte[] Add(byte[] source, string fileName, byte[] fileBytes, string? description)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(fileBytes);

        using var doc = PdfPigDocument.Open(source);
        var trailer = doc.Structure.CrossReferenceTable.Trailer;
        var catalog = doc.Structure.Catalog.CatalogDictionary;
        long nextObj = MaxObjectNumber(doc) + 1;

        var existing = PdfAttachmentCosReader.Read(doc);

        var streamRef = new IndirectReference(nextObj++, 0);
        var filespecRef = new IndirectReference(nextObj++, 0);

        var newObjects = new List<PdfIncrementalWriter.RawObject>(4)
        {
            new(streamRef, WriteEmbeddedFileStream(fileBytes)),
            new(filespecRef, WriteFileSpecDict(fileName, streamRef, description)),
        };

        // Плоский список имён: существующие (кроме коллизии по имени) + новая запись, сортировка по
        // ключу (ordinal) — name-tree требует упорядоченных ключей (ISO 32000-1 §7.9.6).
        var entries = new List<(string Key, IndirectReference Ref)>(existing.Count + 1);
        foreach (var e in existing)
        {
            if (!string.Equals(e.Name, fileName, StringComparison.Ordinal))
            {
                entries.Add((e.Name, e.FilespecRef));
            }
        }

        entries.Add((fileName, filespecRef));
        entries.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));

        return EmitNameTree(source, doc, catalog, trailer.Root, trailer.Size, newObjects, entries, ref nextObj);
    }

    /// <summary>
    /// Удаляет вложение с именем <paramref name="name"/> из name-tree (no-op, если его нет — но всё
    /// равно эмитит валидный апдейт). Осиротевшие file-spec / stream объекты остаются в файле
    /// (инкрементальный апдейт) — это допустимо. Возвращает байты нового PDF.
    /// </summary>
    public static byte[] Remove(byte[] source, string name)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        using var doc = PdfPigDocument.Open(source);
        var trailer = doc.Structure.CrossReferenceTable.Trailer;
        var catalog = doc.Structure.Catalog.CatalogDictionary;
        long nextObj = MaxObjectNumber(doc) + 1;

        var existing = PdfAttachmentCosReader.Read(doc);

        var entries = new List<(string Key, IndirectReference Ref)>(existing.Count);
        foreach (var e in existing)
        {
            if (!string.Equals(e.Name, name, StringComparison.Ordinal))
            {
                entries.Add((e.Name, e.FilespecRef));
            }
        }

        entries.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));

        var newObjects = new List<PdfIncrementalWriter.RawObject>(2);
        return EmitNameTree(source, doc, catalog, trailer.Root, trailer.Size, newObjects, entries, ref nextObj);
    }

    private static byte[] EmitNameTree(
        byte[] source,
        PdfPigDocument doc,
        DictionaryToken catalog,
        IndirectReference rootRef,
        long trailerSize,
        List<PdfIncrementalWriter.RawObject> newObjects,
        List<(string Key, IndirectReference Ref)> entries,
        ref long nextObj)
    {
        var nameTreeRef = new IndirectReference(nextObj++, 0);
        var namesDictRef = new IndirectReference(nextObj++, 0);

        newObjects.Add(new PdfIncrementalWriter.RawObject(nameTreeRef, WriteEmbeddedFilesTree(entries)));

        DictionaryToken? existingNames = ResolveCatalogNames(doc, catalog);
        newObjects.Add(new PdfIncrementalWriter.RawObject(
            namesDictRef, PdfCatalogNamesCosWriter.WriteNamesDict(existingNames, nameTreeRef)));

        string catalogBody = PdfCatalogNamesCosWriter.WriteCatalogWithNames(catalog, namesDictRef);
        var updated = new[] { new PdfIncrementalWriter.RawObject(rootRef, catalogBody) };

        long prevXref = PdfIncrementalWriter.FindLastStartXref(source);
        return PdfIncrementalWriter.Append(
            source, newObjects, updated, prevXref, rootRef.ObjectNumber, trailerSize);
    }

    private static string WriteEmbeddedFileStream(byte[] fileBytes)
    {
        // Несжатый embedded-file поток: /Length = длина, /Params << /Size N >>, без /Filter. Тело
        // потока выписываем как Latin1-строку (1:1 байт↔char для 0–255), т.к. PdfIncrementalWriter
        // re-кодирует тело объекта через Encoding.Latin1 — это byte-preserving для всех байт 0x00–0xFF.
        var sb = new StringBuilder(fileBytes.Length + 96);
        sb.Append("<<\n/Type /EmbeddedFile\n");
        sb.Append(CultureInfo.InvariantCulture, $"/Length {fileBytes.Length}\n");
        sb.Append(CultureInfo.InvariantCulture, $"/Params << /Size {fileBytes.Length} >>\n");
        sb.Append(">>\nstream\n");
        sb.Append(Encoding.Latin1.GetString(fileBytes));
        sb.Append("\nendstream");
        return sb.ToString();
    }

    private static string WriteFileSpecDict(string fileName, IndirectReference streamRef, string? description)
    {
        var sb = new StringBuilder(128);
        sb.Append("<<\n/Type /Filespec\n/F ");
        PdfTextString.Append(sb, fileName);
        sb.Append("\n/UF ");
        PdfTextString.Append(sb, fileName);
        sb.Append(CultureInfo.InvariantCulture, $"\n/EF << /F {streamRef.ObjectNumber} {streamRef.Generation} R >>\n");
        if (!string.IsNullOrEmpty(description))
        {
            sb.Append("/Desc ");
            PdfTextString.Append(sb, description);
            sb.Append('\n');
        }

        sb.Append(">>");
        return sb.ToString();
    }

    private static string WriteEmbeddedFilesTree(List<(string Key, IndirectReference Ref)> entries)
    {
        // Свежий name-tree-лист: << /Names [ (key) ref … ] >>. Пустой список → /Names [] (валидно).
        var sb = new StringBuilder("<<\n/Names [");
        foreach (var (key, reference) in entries)
        {
            sb.Append(' ');
            PdfTextString.Append(sb, key);
            sb.Append(CultureInfo.InvariantCulture, $" {reference.ObjectNumber} {reference.Generation} R");
        }

        sb.Append(" ]\n>>");
        return sb.ToString();
    }

    private static DictionaryToken? ResolveCatalogNames(PdfPigDocument doc, DictionaryToken catalog)
    {
        if (!catalog.TryGet(NamesName, out IToken? raw) || raw is null)
        {
            return null;
        }

        return raw switch
        {
            DictionaryToken inline => inline,
            IndirectReferenceToken iref
                when doc.Structure.GetObject(iref.Data) is ObjectToken { Data: DictionaryToken resolved }
                => resolved,
            _ => null,
        };
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
