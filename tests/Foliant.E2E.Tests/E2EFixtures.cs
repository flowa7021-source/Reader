using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Foliant.E2E.Tests;

/// <summary>
/// Builds (or resolves) one document per format so the E2E tests can drive every loader through the
/// real pipeline. PDF and DjVu reuse the committed binary assets (a real 10-page text PDF; a tiny IW44
/// DjVu); EPUB/FB2/MOBI/PNG are hand-built in a temp directory — compact ports of the per-engine test
/// factories so this project stays self-contained.
/// </summary>
internal static class E2EFixtures
{
    /// <summary>The committed 10-page English text PDF (<c>tests/assets/pdf-text-en-10p.pdf</c>).</summary>
    public static string TextPdf() => SharedAsset(Path.Combine("tests", "assets", "pdf-text-en-10p.pdf"));

    /// <summary>The committed tiny DjVu sample (<c>tests/Foliant.Plugin.DjVu.Tests/assets/sample.djvu</c>).</summary>
    public static string SampleDjvu() =>
        SharedAsset(Path.Combine("tests", "Foliant.Plugin.DjVu.Tests", "assets", "sample.djvu"));

    /// <summary>Writes a minimal valid EPUB 2 (mimetype + container + OPF + NCX + one chapter) and
    /// returns its path.</summary>
    public static string Epub(string dir, string chapterBody = "<h1>E2E EPUB</h1><p>The quick brown fox jumps over the lazy dog.</p>")
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"book-{Guid.NewGuid():N}.epub");

        using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);

        var mime = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var sw = new StreamWriter(mime.Open(), Encoding.ASCII))
        {
            sw.Write("application/epub+zip");
        }

        WriteZipText(zip, "META-INF/container.xml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <container xmlns="urn:oasis:names:tc:opendocument:xmlns:container" version="1.0">
              <rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles>
            </container>
            """);

        WriteZipText(zip, "OEBPS/chapter1.xhtml",
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml"><head><title>C1</title></head><body>{chapterBody}</body></html>
            """);

        WriteZipText(zip, "OEBPS/content.opf",
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="2.0" unique-identifier="id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="id">urn:uuid:{Guid.NewGuid()}</dc:identifier>
                <dc:title>E2E EPUB</dc:title><dc:creator>E2E</dc:creator><dc:language>en</dc:language>
              </metadata>
              <manifest>
                <item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml"/>
                <item id="c1" href="chapter1.xhtml" media-type="application/xhtml+xml"/>
              </manifest>
              <spine toc="ncx"><itemref idref="c1"/></spine>
            </package>
            """);

        WriteZipText(zip, "OEBPS/toc.ncx",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
              <head><meta name="dtb:uid" content="id"/></head>
              <docTitle><text>E2E</text></docTitle>
              <navMap><navPoint id="np1" playOrder="1"><navLabel><text>Start</text></navLabel><content src="chapter1.xhtml"/></navPoint></navMap>
            </ncx>
            """);

        return path;
    }

    /// <summary>Writes a minimal valid FB2 (one body, one section) and returns its path.</summary>
    public static string Fb2(string dir, string paragraph = "The quick brown fox jumps over the lazy dog.")
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"book-{Guid.NewGuid():N}.fb2");

        string xml = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <FictionBook xmlns="http://www.gribuser.ru/xml/fictionbook/2.0">
              <description><title-info>
                <book-title>E2E FB2</book-title>
                <author><first-name>E2E</first-name><last-name>Author</last-name></author>
                <lang>en</lang>
              </title-info></description>
              <body>
                <section><title><p>Chapter One</p></title><p>{System.Net.WebUtility.HtmlEncode(paragraph)}</p></section>
              </body>
            </FictionBook>
            """;
        File.WriteAllText(path, xml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    /// <summary>Writes a minimal valid MOBI (PalmDB + PalmDOC + MOBI header, uncompressed text) and
    /// returns its path. Ported from <c>MobiTestFactory</c>.</summary>
    public static string Mobi(string dir, string htmlBody = "<h1>E2E MOBI</h1><p>The quick brown fox jumps over the lazy dog.</p>", string title = "E2E MOBI")
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"book-{Guid.NewGuid():N}.mobi");
        File.WriteAllBytes(path, BuildMobi(htmlBody, title));
        return path;
    }

    /// <summary>Writes a small solid-colour PNG and returns its path.</summary>
    public static string Png(string dir, int width = 64, int height = 48)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"image-{Guid.NewGuid():N}.png");
        using var image = new Image<Rgba32>(width, height, new Rgba32(100, 149, 237)); // cornflower blue
        image.Save(path, new PngEncoder());
        return path;
    }

    private static byte[] BuildMobi(string htmlBody, string title)
    {
        byte[] titleBytes = Encoding.UTF8.GetBytes(title);
        byte[] text = Encoding.UTF8.GetBytes(htmlBody);
        const int numRecords = 2; // rec0 (headers) + one text record.

        const int palmDocHeaderSize = 16;
        const int mobiHeaderSize = 132;
        int nameOffsetInRec0 = palmDocHeaderSize + mobiHeaderSize;
        byte[] rec0 = new byte[nameOffsetInRec0 + titleBytes.Length];

        BinaryPrimitives.WriteUInt16BigEndian(rec0.AsSpan(0, 2), 1); // compression: none
        BinaryPrimitives.WriteUInt32BigEndian(rec0.AsSpan(4, 4), (uint)text.Length);
        BinaryPrimitives.WriteUInt16BigEndian(rec0.AsSpan(8, 2), 1); // text record count
        BinaryPrimitives.WriteUInt16BigEndian(rec0.AsSpan(10, 2), 4096);

        rec0[16] = (byte)'M';
        rec0[17] = (byte)'O';
        rec0[18] = (byte)'B';
        rec0[19] = (byte)'I';
        BinaryPrimitives.WriteUInt32BigEndian(rec0.AsSpan(20, 4), mobiHeaderSize);
        BinaryPrimitives.WriteUInt32BigEndian(rec0.AsSpan(44, 4), 65001); // UTF-8
        BinaryPrimitives.WriteUInt32BigEndian(rec0.AsSpan(100, 4), (uint)nameOffsetInRec0);
        BinaryPrimitives.WriteUInt32BigEndian(rec0.AsSpan(104, 4), (uint)titleBytes.Length);
        titleBytes.CopyTo(rec0.AsSpan(nameOffsetInRec0));

        const int palmDbHeaderSize = 78;
        const int recordEntrySize = 8;
        int firstRecordOffset = palmDbHeaderSize + (numRecords * recordEntrySize);

        byte[][] records = [rec0, text];
        int[] offsets = new int[numRecords];
        int cursor = firstRecordOffset;
        for (int i = 0; i < numRecords; i++)
        {
            offsets[i] = cursor;
            cursor += records[i].Length;
        }

        byte[] file = new byte[cursor];
        Encoding.ASCII.GetBytes("E2EBook").CopyTo(file.AsSpan(0));
        Encoding.ASCII.GetBytes("BOOK").CopyTo(file.AsSpan(60));
        Encoding.ASCII.GetBytes("MOBI").CopyTo(file.AsSpan(64));
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(76, 2), numRecords);

        for (int i = 0; i < numRecords; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(palmDbHeaderSize + (i * recordEntrySize), 4), (uint)offsets[i]);
        }

        for (int i = 0; i < numRecords; i++)
        {
            records[i].CopyTo(file.AsSpan(offsets[i]));
        }

        return file;
    }

    private static void WriteZipText(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName);
        using var sw = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        sw.Write(content);
    }

    /// <summary>Resolves a path relative to the repository root (located by walking up to
    /// <c>Foliant.sln</c>).</summary>
    private static string SharedAsset(string repoRelativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Foliant.sln")))
            {
                string path = Path.Combine(dir.FullName, repoRelativePath);
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException($"Expected committed E2E asset not found: {repoRelativePath}", path);
                }

                return path;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root (Foliant.sln) from " + AppContext.BaseDirectory);
    }
}
