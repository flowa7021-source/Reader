namespace Foliant.Engines.Mobi;

/// <summary>
/// PalmDOC (LZ77-вариант) разжиматель — формат сжатия текстовых записей в MOBI/PalmDOC.
/// Чистая byte-математика, без I/O; отдельно тестируется.
///
/// Алгоритм (по спецификации PalmDOC):
/// <list type="bullet">
/// <item><c>0x00</c> — литеральный null-байт.</item>
/// <item><c>0x01..0x08</c> — следующие N байт копируются как есть (literal run).</item>
/// <item><c>0x09..0x7F</c> — литеральный ASCII-байт.</item>
/// <item><c>0x80..0xBF</c> — LZ77: следующий байт образует 16-битное значение; distance =
///   <c>(value &gt;&gt; 3) &amp; 0x7FF</c>, length = <c>(value &amp; 7) + 3</c>; копирование из
///   уже разжатого выхода назад на distance.</item>
/// <item><c>0xC0..0xFF</c> — пробел + <c>(byte ^ 0x80)</c> (часто пробел + буква).</item>
/// </list>
/// </summary>
internal static class PalmDocCompression
{
    /// <summary>Тип сжатия из PalmDOC-заголовка (поле compression).</summary>
    public const int CompressionNone = 1;
    public const int CompressionPalmDoc = 2;
    public const int CompressionHuffCdic = 17480;

    /// <summary>Разжать одну PalmDOC-запись. <paramref name="compression"/> = 1 — данные уже
    /// сырые (возвращаются как есть); = 2 — PalmDOC LZ77. HUFF/CDIC (17480) не поддерживается —
    /// бросает <see cref="NotSupportedException"/>.</summary>
    public static byte[] Decompress(ReadOnlySpan<byte> input, int compression)
    {
        if (compression == CompressionNone)
        {
            return input.ToArray();
        }

        if (compression == CompressionHuffCdic)
        {
            throw new NotSupportedException("HUFF/CDIC-compressed MOBI is not supported (Phase 1 reads PalmDOC only).");
        }

        // PalmDOC (или неизвестный — best-effort как PalmDOC).
        var output = new List<byte>(input.Length * 2);
        int i = 0;
        while (i < input.Length)
        {
            int c = input[i++];
            if (c == 0x00)
            {
                output.Add(0x00);
            }
            else if (c <= 0x08)
            {
                // Скопировать следующие c байт буквально.
                for (int k = 0; k < c && i < input.Length; k++)
                {
                    output.Add(input[i++]);
                }
            }
            else if (c <= 0x7F)
            {
                output.Add((byte)c);
            }
            else if (c <= 0xBF)
            {
                // LZ77: нужен ещё один байт.
                if (i >= input.Length)
                {
                    break;
                }
                int value = (c << 8) | input[i++];
                int distance = (value >> 3) & 0x07FF;
                int length = (value & 0x07) + 3;
                if (distance == 0)
                {
                    break; // защита от бесконечного копирования
                }
                for (int k = 0; k < length; k++)
                {
                    int srcIndex = output.Count - distance;
                    if (srcIndex < 0)
                    {
                        break;
                    }
                    output.Add(output[srcIndex]);
                }
            }
            else
            {
                // 0xC0..0xFF: пробел + (c ^ 0x80).
                output.Add((byte)' ');
                output.Add((byte)(c ^ 0x80));
            }
        }

        return [.. output];
    }
}
