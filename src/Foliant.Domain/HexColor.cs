using System.Globalization;

namespace Foliant.Domain;

/// <summary>
/// Разбор hex-цвета (<c>"#RGB"</c> / <c>"#RRGGBB"</c>, с ведущим <c>#</c> или без) в каналы 0..255.
/// Единый помощник для экспортёров аннотаций (FDF и т.п.) и PDF-writer'а, чтобы парсинг цвета
/// не расходился между ними. Чистый, без состояния.
/// </summary>
public static class HexColor
{
    /// <summary>Пытается разобрать <paramref name="hex"/>. При успехе возвращает <c>true</c> и
    /// каналы; иначе <c>false</c> и нули.</summary>
    public static bool TryParse(string? hex, out byte r, out byte g, out byte b)
    {
        r = 0;
        g = 0;
        b = 0;

        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        string h = hex.Trim().TrimStart('#');
        if (h.Length == 3)
        {
            h = string.Concat(h[0], h[0], h[1], h[1], h[2], h[2]);
        }

        if (h.Length != 6)
        {
            return false;
        }

        if (TryChannel(h, 0, out r) && TryChannel(h, 2, out g) && TryChannel(h, 4, out b))
        {
            return true;
        }

        r = 0;
        g = 0;
        b = 0;
        return false;
    }

    private static bool TryChannel(string h, int start, out byte value) =>
        byte.TryParse(h.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
}
