using System.Text;
using Foliant.Domain;
using UglyToad.PdfPig.Tokens;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf;

/// <summary>
/// Читает output-intent'ы из cos-структуры PDF (ISO 32000-1 §14.11.5, таблица 366): резолвит
/// <c>Catalog → /OutputIntents</c> (массив словарей, сам массив может быть indirect'ом) и по каждому
/// элементу-словарю собирает <see cref="PdfOutputIntent"/>: <c>/S</c> (имя без слэша) →
/// <see cref="PdfOutputIntent.Subtype"/>, четыре текстовых поля
/// (<c>/OutputConditionIdentifier</c>, <c>/OutputCondition</c>, <c>/RegistryName</c>, <c>/Info</c>) с
/// разворотом indirect-ссылок и декодированием UTF-16BE-hex (как
/// <see cref="PdfAttachmentCosReader"/> / <see cref="PdfLinkCosReader"/>), и признак наличия потока
/// <c>/DestOutputProfile</c> → <see cref="PdfOutputIntent.HasIccProfile"/> (присутствия ключа
/// достаточно; байты профиля не извлекаются). Не-словарные элементы массива пропускаются. Порядок
/// массива сохраняется. Массив плоский — depth-guard не нужен, но indirect-ссылки резолвятся
/// безопасно. Только чтение.
/// </summary>
internal static class PdfOutputIntentCosReader
{
    private static readonly NameToken OutputIntentsName = NameToken.Create("OutputIntents");
    private static readonly NameToken SName = NameToken.Create("S");
    private static readonly NameToken OutputConditionIdentifierName = NameToken.Create("OutputConditionIdentifier");
    private static readonly NameToken OutputConditionName = NameToken.Create("OutputCondition");
    private static readonly NameToken RegistryNameName = NameToken.Create("RegistryName");
    private static readonly NameToken InfoName = NameToken.Create("Info");
    private static readonly NameToken DestOutputProfileName = NameToken.Create("DestOutputProfile");

    /// <summary>Читает все output-intent'ы документа. Нет <c>/OutputIntents</c> → пустой список.
    /// Результат — в порядке элементов массива.</summary>
    public static IReadOnlyList<PdfOutputIntent> Read(byte[] pdfBytes)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);

        using var doc = PdfPigDocument.Open(pdfBytes);
        return Read(doc);
    }

    /// <summary>Перегрузка для уже открытого документа (на случай переиспользования без повторного
    /// открытия PDF). Семантика идентична <see cref="Read(byte[])"/>.</summary>
    public static IReadOnlyList<PdfOutputIntent> Read(PdfPigDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var catalog = doc.Structure.Catalog.CatalogDictionary;
        if (!TryResolveArray(doc, catalog, OutputIntentsName, out var intents))
        {
            return [];
        }

        var sink = new List<PdfOutputIntent>(intents.Length);
        foreach (var element in intents.Data)
        {
            // Элемент массива — output-intent словарь, inline или indirect. Не-словарные элементы
            // (мусор, ссылка не на словарь) пропускаем.
            if (Resolve(doc, element) is not DictionaryToken intentDict)
            {
                continue;
            }

            sink.Add(ReadIntent(doc, intentDict));
        }

        return sink;
    }

    private static PdfOutputIntent ReadIntent(PdfPigDocument doc, DictionaryToken intentDict)
    {
        string subtype = ReadName(intentDict, SName);
        string? identifier = ReadText(doc, intentDict, OutputConditionIdentifierName);
        string? condition = ReadText(doc, intentDict, OutputConditionName);
        string? registry = ReadText(doc, intentDict, RegistryNameName);
        string? info = ReadText(doc, intentDict, InfoName);
        bool hasIcc = intentDict.ContainsKey(DestOutputProfileName);
        return new PdfOutputIntent(subtype, identifier, condition, registry, info, hasIcc);
    }

    private static string ReadName(DictionaryToken dict, NameToken key) =>
        dict.TryGet(key, out NameToken? value) && value is not null ? value.Data : string.Empty;

    private static string? ReadText(PdfPigDocument doc, DictionaryToken dict, NameToken key)
    {
        if (!dict.TryGet(key, out IToken? raw) || raw is null)
        {
            return null;
        }

        return Resolve(doc, raw) switch
        {
            HexToken h => DecodeHexString(h),
            StringToken str => str.Data,
            _ => null,
        };
    }

    private static string DecodeHexString(HexToken hex)
    {
        // UTF-16BE с BOM декодируем явно из сырых байт — не полагаемся на то, что нижележащий токенайзер
        // уже распознал BOM (паттерн как в PdfAttachmentCosReader / PdfLinkCosReader).
        var span = hex.Memory.Span;
        if (span.Length >= 2 && span[0] == 0xFE && span[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(span[2..]);
        }

        return hex.Data;
    }

    private static IToken Resolve(PdfPigDocument doc, IToken token)
    {
        if (token is IndirectReferenceToken iref &&
            doc.Structure.GetObject(iref.Data) is ObjectToken { Data: IToken resolved })
        {
            return resolved;
        }

        return token;
    }

    private static bool TryResolveArray(
        PdfPigDocument doc, DictionaryToken parent, NameToken key, out ArrayToken array)
    {
        array = null!;
        if (!parent.TryGet(key, out IToken? raw) || raw is null)
        {
            return false;
        }

        switch (raw)
        {
            case ArrayToken inline:
                array = inline;
                return true;
            case IndirectReferenceToken iref
                when doc.Structure.GetObject(iref.Data) is ObjectToken { Data: ArrayToken resolved }:
                array = resolved;
                return true;
            default:
                return false;
        }
    }
}
