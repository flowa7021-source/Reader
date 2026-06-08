using System.Buffers.Binary;
using System.Text;

namespace Foliant.Engines.Mobi.Tests;

/// <summary>
/// A hand-built corpus of <b>malformed / hostile</b> MOBI (PalmDB) payloads used to prove the MOBI
/// opener's robustness contract: a too-small, garbage, structurally-broken, overflowing or
/// unsupported-compression container must complete <i>promptly</i> — either parsing into a usable
/// document or throwing a <b>tame, catchable</b> exception (<see cref="InvalidDataException"/>) — never
/// a <see cref="StackOverflowException"/>, hang, OOM or process crash. Mirrors
/// <c>Foliant.Engines.Pdf.Tests.MalformedPdfCorpus</c> for the MOBI/PalmDB container, building on
/// <see cref="MobiTestFactory"/> for the valid baseline that the corrupting variants mutate.
///
/// <para>Every payload is in-memory bytes (no temp file needed — fixtures are fed to
/// <c>MobiDocument.Parse(bytes, renderer)</c>). Each is surfaced by a stable, human-readable name
/// through <c>[MemberData]</c>.</para>
/// </summary>
internal static class MalformedMobiCorpus
{
    private const int PalmDbHeaderSize = 78;
    private const int RecordEntrySize = 8;
    private const int HuffCdicCompression = 17480;

    /// <summary>Enumerates the full corpus as <c>(Name, Bytes)</c> pairs.</summary>
    /// <returns>Every malformed MOBI payload in the corpus.</returns>
    public static IEnumerable<(string Name, byte[] Bytes)> All()
    {
        yield return ("zero-byte", []);
        yield return ("too-small-below-header", new byte[10]);
        yield return ("garbage", Garbage());
        yield return ("valid-header-zero-records", ZeroRecords());
        yield return ("truncated-record-list", TruncatedRecordList());
        yield return ("numrecords-huge-file-short", NumRecordsHugeFileShort());
        yield return ("overflowing-record-offsets", OverflowingRecordOffsets());
        yield return ("record0-huff-cdic-compression", Record0HuffCdic());
        yield return ("truncated-text-records", TruncatedTextRecords());
        yield return ("control-valid-baseline", ControlValid());
    }

    // ~200 bytes of non-PalmDB filler (header-sized but meaningless; record list overruns the buffer).
    private static byte[] Garbage()
    {
        var bytes = new byte[200];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(((i * 53) + 7) & 0xFF);
        }

        // Force a non-zero numRecords so the parser attempts to walk a record list it cannot satisfy.
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(76, 2), 99);
        return bytes;
    }

    // A bare 78-byte PalmDB header that declares zero records.
    private static byte[] ZeroRecords()
    {
        var bytes = new byte[PalmDbHeaderSize];
        Encoding.ASCII.GetBytes("BOOK").CopyTo(bytes.AsSpan(60));
        Encoding.ASCII.GetBytes("MOBI").CopyTo(bytes.AsSpan(64));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(76, 2), 0);
        return bytes;
    }

    // Declares several records but the file ends before the record-info list is complete.
    private static byte[] TruncatedRecordList()
    {
        // Header + room for only one-and-a-bit record entries, while claiming 4 records.
        var bytes = new byte[PalmDbHeaderSize + RecordEntrySize + 3];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(76, 2), 4);
        return bytes;
    }

    // numRecords is enormous but the file is tiny — the record-list walk must bail with InvalidData.
    private static byte[] NumRecordsHugeFileShort()
    {
        var bytes = new byte[100];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(76, 2), 5000);
        return bytes;
    }

    // A valid baseline whose record-0 offset is pushed far past EOF.
    private static byte[] OverflowingRecordOffsets()
    {
        byte[] mobi = MobiTestFactory.Build("<html><body><p>ok</p></body></html>");
        BinaryPrimitives.WriteUInt32BigEndian(mobi.AsSpan(PalmDbHeaderSize, 4), 0x00FF_FFFF);
        return mobi;
    }

    // A valid baseline whose record-0 declares HUFF/CDIC compression (type 17480) — unsupported.
    private static byte[] Record0HuffCdic()
    {
        byte[] mobi = MobiTestFactory.Build("<html><body><p>ok</p></body></html>");
        int rec0Off = (int)BinaryPrimitives.ReadUInt32BigEndian(mobi.AsSpan(PalmDbHeaderSize, 4));
        BinaryPrimitives.WriteUInt16BigEndian(mobi.AsSpan(rec0Off, 2), HuffCdicCompression);
        return mobi;
    }

    // A valid baseline truncated a few bytes into the first text record (record-0 still intact).
    private static byte[] TruncatedTextRecords()
    {
        byte[] mobi = MobiTestFactory.Build("<html><body><p>hello world, this is the body text</p></body></html>");
        int rec1Off = (int)BinaryPrimitives.ReadUInt32BigEndian(mobi.AsSpan(PalmDbHeaderSize + RecordEntrySize, 4));
        int cut = Math.Min(mobi.Length, rec1Off + 3);
        return mobi[..cut];
    }

    // A clean, valid MOBI — the control that must parse into a real one-page document.
    private static byte[] ControlValid() =>
        MobiTestFactory.Build("<html><body><h1>Title</h1><p>Body text for the control fixture.</p></body></html>");
}
