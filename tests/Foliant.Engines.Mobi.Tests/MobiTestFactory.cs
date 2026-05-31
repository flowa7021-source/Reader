using System.Buffers.Binary;
using System.Text;

namespace Foliant.Engines.Mobi.Tests;

/// <summary>
/// Собирает минимальный валидный PalmDB/MOBI-контейнер в памяти для тестов: PalmDB-заголовок
/// (78 байт) + таблица записей + запись 0 (PalmDOC + MOBI header) + одна или несколько текстовых
/// записей. Текст хранится несжатым (compression = 1), чтобы тесты не зависели от точности
/// PalmDOC-кодировщика — разжиматель тестируется отдельно.
/// </summary>
internal static class MobiTestFactory
{
    /// <summary>Построить MOBI-байты с одной текстовой записью (compression none) и заданным
    /// HTML-телом + full-name (title).</summary>
    public static byte[] Build(string htmlBody, string title = "Test Book")
        => Build([htmlBody], title);

    /// <summary>Построить MOBI с несколькими текстовыми записями — каждая станет отдельной
    /// «страницей».</summary>
    public static byte[] Build(IReadOnlyList<string> htmlRecords, string title = "Test Book")
    {
        byte[] titleBytes = Encoding.UTF8.GetBytes(title);
        byte[][] textRecords = [.. htmlRecords.Select(Encoding.UTF8.GetBytes)];
        int numRecords = 1 + textRecords.Length;

        // ── Запись 0: PalmDOC header (16) + MOBI header (≥108) + full-name ──
        // MOBI header нам нужен минимум до offset 108 (fullNameOffset/Length по rec0-offset 100/104),
        // плюс text-encoding по rec0-offset 44. Выделим 132 байта на MOBI header + name.
        const int PalmDocHeaderSize = 16;
        const int MobiHeaderSize = 132;
        int nameOffsetInRec0 = PalmDocHeaderSize + MobiHeaderSize;
        int rec0Size = nameOffsetInRec0 + titleBytes.Length;

        byte[] rec0 = new byte[rec0Size];
        // PalmDOC header: compression=1 (none), textLength, recordCount, recordSize=4096.
        BinaryPrimitives.WriteUInt16BigEndian(rec0.AsSpan(0, 2), 1); // compression none
        int totalTextLen = textRecords.Sum(r => r.Length);
        BinaryPrimitives.WriteUInt32BigEndian(rec0.AsSpan(4, 4), (uint)totalTextLen);
        BinaryPrimitives.WriteUInt16BigEndian(rec0.AsSpan(8, 2), (ushort)textRecords.Length); // record count
        BinaryPrimitives.WriteUInt16BigEndian(rec0.AsSpan(10, 2), 4096); // record size

        // MOBI header at rec0-offset 16.
        rec0[16] = (byte)'M';
        rec0[17] = (byte)'O';
        rec0[18] = (byte)'B';
        rec0[19] = (byte)'I';
        BinaryPrimitives.WriteUInt32BigEndian(rec0.AsSpan(20, 4), (uint)MobiHeaderSize); // header length
        BinaryPrimitives.WriteUInt32BigEndian(rec0.AsSpan(44, 4), 65001); // text encoding = UTF-8
        // fullName offset/length отсчитываются от начала записи 0 в файле.
        BinaryPrimitives.WriteUInt32BigEndian(rec0.AsSpan(100, 4), (uint)nameOffsetInRec0);
        BinaryPrimitives.WriteUInt32BigEndian(rec0.AsSpan(104, 4), (uint)titleBytes.Length);
        titleBytes.CopyTo(rec0.AsSpan(nameOffsetInRec0));

        // ── Сборка ──
        const int PalmDbHeaderSize = 78;
        const int RecordEntrySize = 8;
        int recordTableSize = numRecords * RecordEntrySize;
        int firstRecordOffset = PalmDbHeaderSize + recordTableSize;

        var allRecords = new List<byte[]> { rec0 };
        allRecords.AddRange(textRecords);

        // Offsets.
        int[] offsets = new int[numRecords];
        int cursor = firstRecordOffset;
        for (int i = 0; i < numRecords; i++)
        {
            offsets[i] = cursor;
            cursor += allRecords[i].Length;
        }

        int totalSize = cursor;
        byte[] file = new byte[totalSize];

        // PalmDB header: name[32], затем поля; type/creator at 60/64, numRecords at 76.
        Encoding.ASCII.GetBytes("TestBook").CopyTo(file.AsSpan(0));
        Encoding.ASCII.GetBytes("BOOK").CopyTo(file.AsSpan(60));
        Encoding.ASCII.GetBytes("MOBI").CopyTo(file.AsSpan(64));
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(76, 2), (ushort)numRecords);

        // Record info list.
        for (int i = 0; i < numRecords; i++)
        {
            int entryPos = PalmDbHeaderSize + i * RecordEntrySize;
            BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(entryPos, 4), (uint)offsets[i]);
            // attributes+uniqueID — оставляем нулями.
        }

        // Records.
        for (int i = 0; i < numRecords; i++)
        {
            allRecords[i].CopyTo(file.AsSpan(offsets[i]));
        }

        return file;
    }

    /// <summary>Записать MOBI на диск в указанную папку и вернуть путь.</summary>
    public static string WriteToFile(string dir, string fileName, string htmlBody, string title = "Test Book")
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, fileName);
        File.WriteAllBytes(path, Build(htmlBody, title));
        return path;
    }
}
