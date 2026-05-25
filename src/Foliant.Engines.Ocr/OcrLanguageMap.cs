namespace Foliant.Engines.Ocr;

/// <summary>Какой набор моделей PaddleOCR использовать для распознавания.</summary>
internal enum OcrModelKind
{
    /// <summary>Латиница (eng, deu, fra, spa, ita, …) — модель распознавания «latin».</summary>
    Latin,

    /// <summary>Кириллица (rus, ukr, bel, kaz, …) — модель распознавания «cyrillic».</summary>
    Cyrillic,
}

/// <summary>
/// Чистый маппинг языковых кодов в стиле Tesseract (<c>"eng+rus"</c>) на набор моделей
/// PaddleOCR. Вынесен отдельно от движка, чтобы покрывать unit-тестами без нативки.
///
/// Правило: если среди запрошенных языков есть хоть один кириллический — берём модель
/// «cyrillic» (она распознаёт и латиницу тоже), иначе «latin». Это покрывает приоритетную
/// для проекта связку рус+eng одной моделью.
/// </summary>
internal static class OcrLanguageMap
{
    private static readonly HashSet<string> CyrillicCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "rus", "ukr", "bel", "kaz", "bul", "srp", "mkd", "mon", "tgk", "kir",
    };

    public static OcrModelKind Resolve(string languages)
    {
        ArgumentNullException.ThrowIfNull(languages);

        foreach (var token in languages.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (CyrillicCodes.Contains(token))
            {
                return OcrModelKind.Cyrillic;
            }
        }

        return OcrModelKind.Latin;
    }
}
