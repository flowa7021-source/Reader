using System.Globalization;
using System.Text;
using Foliant.Domain;

namespace Foliant.Application.Services;

/// <summary>
/// Импорт аннотаций из FDF (Adobe Forms Data Format) — обратная операция к
/// <see cref="FdfAnnotationExporter"/>, закрывает round-trip обмен с Acrobat (Q-F17): пользователь
/// экспортирует аннотации в FDF, правит в Acrobat, сохраняет FDF — мы читаем результат сюда.
///
/// Минимальный cos-токенайзер scope'нут в этот файл (PdfPig свой не экспонирует): числа, имена,
/// hex-/literal-строки, массивы, словари, references — больше FDF не требует (streams/xref для
/// аннотаций отсутствуют). Парсинг — best-effort, как в XFDF-importer'е: битые элементы
/// пропускаются; невалидный заголовок — единственная фатальная ошибка.
/// </summary>
public sealed class FdfAnnotationImporter : IAnnotationImporter
{
    public string FormatName => "FDF";

    public string FileExtension => "fdf";

    public IReadOnlyList<Annotation> Import(string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        // FDF — байт-ориентирован (литералы могут содержать non-ASCII октеты). Latin-1 round-trip'ит
        // каждый char ↔ byte 1:1 — это безопасно, пока вызывающий читает файл как Latin-1 или как
        // UTF-8 для ASCII-чистого FDF (наш экспортёр эмитит всё в ASCII + hex-строках).
        byte[] bytes = Encoding.Latin1.GetBytes(content);
        if (!HasFdfHeader(bytes))
        {
            throw new FormatException("Not an FDF document (missing %FDF- header).");
        }

        var parser = new CosParser(bytes);
        if (!parser.AdvanceToFirstDictionary())
        {
            return [];
        }

        var root = (CosDict)parser.ReadValue();
        if (root.Get<CosDict>("FDF") is not { } fdf || fdf.Get<CosArray>("Annots") is not { } annots)
        {
            return [];
        }

        var result = new List<Annotation>(annots.Items.Count);
        foreach (var item in annots.Items)
        {
            if (item is CosDict dict && FdfAnnotationMapping.ToAnnotation(dict) is { } annotation)
            {
                result.Add(annotation);
            }
        }

        return result;
    }

    private static bool HasFdfHeader(byte[] bytes)
    {
        // Header допустимо смещён на пару байт (BOM/whitespace), но наш экспортёр пишет без BOM —
        // ищем "%FDF-" в первых 16 байтах.
        int limit = Math.Min(bytes.Length, 16);
        for (int i = 0; i <= limit - 5; i++)
        {
            if (bytes[i] == (byte)'%' && bytes[i + 1] == (byte)'F'
                && bytes[i + 2] == (byte)'D' && bytes[i + 3] == (byte)'F' && bytes[i + 4] == (byte)'-')
            {
                return true;
            }
        }

        return false;
    }
}

// Все хелперы, чья сигнатура трогает file-локальные cos-типы, ДОЛЖНЫ быть в file-scope
// (CS9051 — file-type не может фигурировать в сигнатуре non-file-type'а).
file static class FdfAnnotationMapping
{
    public static Annotation? ToAnnotation(CosDict d)
    {
        if (d.Get<CosName>("Subtype") is not { } subtype || d.Get<CosNumber>("Page") is not { } page)
        {
            return null;
        }

        int pageIndex = (int)page.Value;
        string color = ReadColor(d);
        DateTimeOffset createdAt = ReadDate(d, "CreationDate") ?? DateTimeOffset.UtcNow;

        Annotation? core = subtype.Value switch
        {
            "Highlight" => ReadRect(d) is { } b
                ? Annotation.Highlight(pageIndex, b, color, createdAt)
                : null,
            "Text" => ReadRect(d) is { } b
                ? Annotation.StickyNote(pageIndex, b, ReadText(d, "Contents"), color, createdAt)
                : null,
            "Ink" => ReadInkPoints(d) is { Count: > 0 } pts
                ? Annotation.Freehand(pageIndex, pts, color, createdAt)
                : null,
            _ => null,
        };

        if (core is null)
        {
            return null;
        }

        // Метаданные навешиваем поверх — оставляем дефолтный null, если соответствующий ключ
        // в FDF отсутствует или пуст.
        return core with
        {
            ModifiedAt = ReadDate(d, "M"),
            Author = NonEmpty(ReadText(d, "T")),
            Subject = NonEmpty(ReadText(d, "Subj")),
        };
    }

    private static string? NonEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

    private static AnnotationRect? ReadRect(CosDict d)
    {
        if (d.Get<CosArray>("Rect") is not { Items.Count: 4 } arr)
        {
            return null;
        }

        if (!TryNumber(arr.Items[0], out double xll)
            || !TryNumber(arr.Items[1], out double yll)
            || !TryNumber(arr.Items[2], out double xur)
            || !TryNumber(arr.Items[3], out double yur))
        {
            return null;
        }

        return new AnnotationRect(xll, yll, xur - xll, yur - yll);
    }

    private static string ReadColor(CosDict d)
    {
        if (d.Get<CosArray>("C") is not { } arr || arr.Items.Count < 3
            || !TryNumber(arr.Items[0], out double r)
            || !TryNumber(arr.Items[1], out double g)
            || !TryNumber(arr.Items[2], out double b))
        {
            return "#000000";
        }

        return string.Create(CultureInfo.InvariantCulture, $"#{ToByte(r):X2}{ToByte(g):X2}{ToByte(b):X2}");
    }

    private static byte ToByte(double channel01) => (byte)Math.Clamp((int)Math.Round(channel01 * 255.0), 0, 255);

    private static string ReadText(CosDict d, string key) =>
        d.Get<CosString>(key) is { } s ? DecodePdfText(s.Bytes) : string.Empty;

    private static List<AnnotationPoint>? ReadInkPoints(CosDict d)
    {
        if (d.Get<CosArray>("InkList") is not { } outer)
        {
            return null;
        }

        var points = new List<AnnotationPoint>();
        foreach (var sub in outer.Items)
        {
            if (sub is not CosArray stroke)
            {
                continue;
            }

            for (int i = 0; i + 1 < stroke.Items.Count; i += 2)
            {
                if (TryNumber(stroke.Items[i], out double x) && TryNumber(stroke.Items[i + 1], out double y))
                {
                    points.Add(new AnnotationPoint(x, y));
                }
            }
        }

        return points.Count == 0 ? null : points;
    }

    private static DateTimeOffset? ReadDate(CosDict d, string key)
    {
        if (d.Get<CosString>(key) is not { } s)
        {
            return null;
        }

        string raw = DecodePdfText(s.Bytes);
        // Формат: "D:YYYYMMDDHHMMSSZ" или "D:YYYYMMDDHHMMSS+HH'MM'". Берём 14 цифр после "D:",
        // считаем UTC — нам нужна стабильная позиция во времени, а не точная зона из FDF.
        if (raw.StartsWith("D:", StringComparison.Ordinal))
        {
            raw = raw[2..];
        }

        if (raw.Length >= 14
            && DateTime.TryParseExact(
                raw[..14], "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTime dt))
        {
            return new DateTimeOffset(dt, TimeSpan.Zero);
        }

        return null;
    }

    private static bool TryNumber(CosValue v, out double value)
    {
        if (v is CosNumber n)
        {
            value = n.Value;
            return true;
        }

        value = 0;
        return false;
    }

    private static string DecodePdfText(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        // Без BOM — приближаем PDFDocEncoding к Latin-1 (Acrobat для не-ASCII всё равно
        // эмитит UTF-16 hex-строки, так что это запасной путь).
        return Encoding.Latin1.GetString(bytes);
    }
}

// ─── Minimal cos value model (file-local: не торчит наружу из этого файла). ───

file abstract record CosValue;

file sealed record CosNumber(double Value) : CosValue;

file sealed record CosName(string Value) : CosValue;

file sealed record CosString(byte[] Bytes) : CosValue;

file sealed record CosBool(bool Value) : CosValue;

file sealed record CosNull : CosValue;

file sealed record CosArray(IReadOnlyList<CosValue> Items) : CosValue;

file sealed record CosDict(IReadOnlyDictionary<string, CosValue> Entries) : CosValue
{
    public T? Get<T>(string name) where T : CosValue =>
        Entries.TryGetValue(name, out var v) ? v as T : null;
}

file sealed record CosRef(int Object, int Generation) : CosValue;

file sealed class CosParser
{
    private readonly byte[] _data;
    private int _pos;

    public CosParser(byte[] data) => _data = data;

    /// <summary>Скипает заголовок и `1 0 obj`, доходит до первого <c>&lt;&lt;</c>.</summary>
    public bool AdvanceToFirstDictionary()
    {
        while (_pos < _data.Length - 1)
        {
            if (_data[_pos] == (byte)'<' && _data[_pos + 1] == (byte)'<')
            {
                return true;
            }

            _pos++;
        }

        return false;
    }

    public CosValue ReadValue()
    {
        SkipWhitespaceAndComments();
        if (_pos >= _data.Length)
        {
            throw new FormatException("Unexpected end of FDF.");
        }

        byte b = _data[_pos];
        return b switch
        {
            (byte)'/' => ReadName(),
            (byte)'(' => ReadLiteralString(),
            (byte)'[' => ReadArray(),
            (byte)'<' when _pos + 1 < _data.Length && _data[_pos + 1] == (byte)'<' => ReadDictionary(),
            (byte)'<' => ReadHexString(),
            (byte)'t' or (byte)'f' => ReadBoolean(),
            (byte)'n' => ReadNullLiteral(),
            _ when IsNumberStart(b) => ReadNumberOrReference(),
            _ => throw new FormatException($"Unexpected byte 0x{b:X2} at FDF offset {_pos}."),
        };
    }

    private CosName ReadName()
    {
        Expect((byte)'/');
        int start = _pos;
        while (_pos < _data.Length && !IsWhitespace(_data[_pos]) && !IsDelimiter(_data[_pos]))
        {
            _pos++;
        }

        return new CosName(Encoding.Latin1.GetString(_data, start, _pos - start));
    }

    private CosString ReadLiteralString()
    {
        Expect((byte)'(');
        var buffer = new List<byte>();
        int depth = 1;
        while (_pos < _data.Length)
        {
            byte b = _data[_pos++];
            if (b == (byte)'\\')
            {
                if (_pos >= _data.Length)
                {
                    throw new FormatException("Truncated escape in FDF literal string.");
                }

                byte next = _data[_pos++];
                switch (next)
                {
                    case (byte)'n': buffer.Add((byte)'\n'); break;
                    case (byte)'r': buffer.Add((byte)'\r'); break;
                    case (byte)'t': buffer.Add((byte)'\t'); break;
                    case (byte)'b': buffer.Add((byte)'\b'); break;
                    case (byte)'f': buffer.Add((byte)'\f'); break;
                    case (byte)'(': buffer.Add((byte)'('); break;
                    case (byte)')': buffer.Add((byte)')'); break;
                    case (byte)'\\': buffer.Add((byte)'\\'); break;
                    case (byte)'\n': /* line continuation */ break;
                    case (byte)'\r':
                        if (_pos < _data.Length && _data[_pos] == (byte)'\n')
                        {
                            _pos++;
                        }

                        break;
                    default:
                        if (next >= (byte)'0' && next <= (byte)'7')
                        {
                            int oct = next - (byte)'0';
                            for (int k = 0; k < 2 && _pos < _data.Length
                                && _data[_pos] >= (byte)'0' && _data[_pos] <= (byte)'7'; k++)
                            {
                                oct = (oct << 3) | (_data[_pos++] - (byte)'0');
                            }

                            buffer.Add((byte)(oct & 0xFF));
                        }
                        else
                        {
                            buffer.Add(next);
                        }

                        break;
                }
            }
            else if (b == (byte)'(')
            {
                depth++;
                buffer.Add(b);
            }
            else if (b == (byte)')')
            {
                depth--;
                if (depth == 0)
                {
                    return new CosString([.. buffer]);
                }

                buffer.Add(b);
            }
            else
            {
                buffer.Add(b);
            }
        }

        throw new FormatException("Unterminated literal string in FDF.");
    }

    private CosString ReadHexString()
    {
        Expect((byte)'<');
        var buffer = new List<byte>();
        int high = -1;
        while (_pos < _data.Length)
        {
            byte b = _data[_pos++];
            if (b == (byte)'>')
            {
                if (high >= 0)
                {
                    buffer.Add((byte)(high << 4));
                }

                return new CosString([.. buffer]);
            }

            if (IsWhitespace(b))
            {
                continue;
            }

            int v = HexValue(b);
            if (v < 0)
            {
                throw new FormatException($"Bad hex digit 0x{b:X2} in FDF hex string at offset {_pos - 1}.");
            }

            if (high < 0)
            {
                high = v;
            }
            else
            {
                buffer.Add((byte)((high << 4) | v));
                high = -1;
            }
        }

        throw new FormatException("Unterminated hex string in FDF.");
    }

    private CosArray ReadArray()
    {
        Expect((byte)'[');
        var items = new List<CosValue>();
        while (true)
        {
            SkipWhitespaceAndComments();
            if (_pos >= _data.Length)
            {
                throw new FormatException("Unterminated array in FDF.");
            }

            if (_data[_pos] == (byte)']')
            {
                _pos++;
                return new CosArray(items);
            }

            items.Add(ReadValue());
        }
    }

    private CosDict ReadDictionary()
    {
        Expect((byte)'<');
        Expect((byte)'<');
        var entries = new Dictionary<string, CosValue>(StringComparer.Ordinal);
        while (true)
        {
            SkipWhitespaceAndComments();
            if (_pos + 1 < _data.Length && _data[_pos] == (byte)'>' && _data[_pos + 1] == (byte)'>')
            {
                _pos += 2;
                return new CosDict(entries);
            }

            if (_pos >= _data.Length)
            {
                throw new FormatException("Unterminated dictionary in FDF.");
            }

            if (_data[_pos] != (byte)'/')
            {
                throw new FormatException($"Expected name in dictionary at FDF offset {_pos}.");
            }

            string key = ReadName().Value;
            entries[key] = ReadValue();
        }
    }

    private CosBool ReadBoolean()
    {
        if (Match("true"))
        {
            return new CosBool(true);
        }

        if (Match("false"))
        {
            return new CosBool(false);
        }

        throw new FormatException($"Expected boolean at FDF offset {_pos}.");
    }

    private CosNull ReadNullLiteral()
    {
        if (Match("null"))
        {
            return new CosNull();
        }

        throw new FormatException($"Expected null at FDF offset {_pos}.");
    }

    private CosValue ReadNumberOrReference()
    {
        double value = ReadNumberLiteral();

        // Indirect-reference форма "N G R" — после числа смотрим, нет ли ещё одного числа + 'R'.
        // Если нет — откатываемся к позиции сразу после первого числа, чтобы следующий toplevel
        // распарсил оставшийся whitespace.
        int savedPos = _pos;
        SkipWhitespaceAndComments();
        if (_pos < _data.Length && IsDigit(_data[_pos]))
        {
            double gen = ReadNumberLiteral();
            SkipWhitespaceAndComments();
            if (_pos < _data.Length && _data[_pos] == (byte)'R')
            {
                _pos++;
                return new CosRef((int)value, (int)gen);
            }
        }

        _pos = savedPos;
        return new CosNumber(value);
    }

    private double ReadNumberLiteral()
    {
        int start = _pos;
        if (_data[_pos] == (byte)'+' || _data[_pos] == (byte)'-')
        {
            _pos++;
        }

        bool sawDigit = false;
        while (_pos < _data.Length && (IsDigit(_data[_pos]) || _data[_pos] == (byte)'.'))
        {
            if (IsDigit(_data[_pos]))
            {
                sawDigit = true;
            }

            _pos++;
        }

        if (!sawDigit)
        {
            throw new FormatException($"Expected number at FDF offset {start}.");
        }

        return double.Parse(
            Encoding.Latin1.GetString(_data, start, _pos - start),
            CultureInfo.InvariantCulture);
    }

    private void SkipWhitespaceAndComments()
    {
        while (_pos < _data.Length)
        {
            byte b = _data[_pos];
            if (IsWhitespace(b))
            {
                _pos++;
                continue;
            }

            if (b == (byte)'%')
            {
                while (_pos < _data.Length && _data[_pos] != (byte)'\n' && _data[_pos] != (byte)'\r')
                {
                    _pos++;
                }

                continue;
            }

            break;
        }
    }

    private void Expect(byte b)
    {
        if (_pos >= _data.Length || _data[_pos] != b)
        {
            throw new FormatException($"Expected '{(char)b}' at FDF offset {_pos}.");
        }

        _pos++;
    }

    private bool Match(string s)
    {
        if (_pos + s.Length > _data.Length)
        {
            return false;
        }

        for (int i = 0; i < s.Length; i++)
        {
            if (_data[_pos + i] != (byte)s[i])
            {
                return false;
            }
        }

        _pos += s.Length;
        return true;
    }

    private static bool IsWhitespace(byte b) => b is 0 or 9 or 10 or 12 or 13 or 32;

    private static bool IsDelimiter(byte b) =>
        b is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>'
        or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}'
        or (byte)'/' or (byte)'%';

    private static bool IsDigit(byte b) => b >= (byte)'0' && b <= (byte)'9';

    private static bool IsNumberStart(byte b) =>
        IsDigit(b) || b == (byte)'+' || b == (byte)'-' || b == (byte)'.';

    private static int HexValue(byte b) =>
        b >= (byte)'0' && b <= (byte)'9' ? b - (byte)'0'
        : b >= (byte)'A' && b <= (byte)'F' ? b - (byte)'A' + 10
        : b >= (byte)'a' && b <= (byte)'f' ? b - (byte)'a' + 10
        : -1;
}
