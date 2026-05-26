namespace Foliant.Domain;

/// <summary>
/// Чистые цвето-преобразования BGRA32-буфера рендера по теме просмотра, общие для всех
/// движков (PDFium, DjVu). <see cref="RenderTheme.Dark"/> — инверсия каналов;
/// <see cref="RenderTheme.HighContrast"/> — инверсия + растяжение контраста вокруг середины
/// (тёмное темнее, светлое светлее). Альфа-канал не изменяется. Буфер правится на месте.
/// </summary>
public static class RenderColorMap
{
    private const double HighContrastFactor = 1.6;

    /// <summary>Применить тему к BGRA32-буферу на месте (<see cref="RenderTheme.Original"/> — no-op).</summary>
    public static void ApplyTheme(Span<byte> bgra, RenderTheme theme)
    {
        switch (theme)
        {
            case RenderTheme.Dark:
                Invert(bgra);
                break;
            case RenderTheme.HighContrast:
                ApplyHighContrast(bgra);
                break;
            default:
                break;
        }
    }

    /// <summary>Инвертировать каналы B/G/R; альфа без изменений.</summary>
    public static void Invert(Span<byte> bgra)
    {
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            bgra[i] = (byte)(255 - bgra[i]);
            bgra[i + 1] = (byte)(255 - bgra[i + 1]);
            bgra[i + 2] = (byte)(255 - bgra[i + 2]);
        }
    }

    /// <summary>Высокий контраст: инверсия + растяжение вокруг 128; альфа без изменений.</summary>
    public static void ApplyHighContrast(Span<byte> bgra)
    {
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            bgra[i] = Stretch(bgra[i]);
            bgra[i + 1] = Stretch(bgra[i + 1]);
            bgra[i + 2] = Stretch(bgra[i + 2]);
        }
    }

    private static byte Stretch(byte channel)
    {
        int inverted = 255 - channel;
        int stretched = 128 + (int)((inverted - 128) * HighContrastFactor);
        return (byte)Math.Clamp(stretched, 0, 255);
    }
}
