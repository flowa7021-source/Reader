using System.IO.Compression;
using System.Text;

namespace Foliant.Engines.Epub.Tests;

/// <summary>
/// A hand-built corpus of <b>malformed / hostile</b> EPUB payloads used to prove the e-book opener's
/// robustness contract: a corrupt, truncated, structurally-broken or pathological EPUB must complete
/// <i>promptly</i> — either opening into a usable document or throwing a <b>tame, catchable</b>
/// exception — never a <see cref="StackOverflowException"/>, hang, OOM or process crash. This mirrors
/// <c>Foliant.Engines.Pdf.Tests.MalformedPdfCorpus</c> for the EPUB container (zip → OCF →
/// container.xml → OPF → spine → chapter XHTML), generalising <see cref="EpubTestFactory"/>'s valid
/// builder into deliberately-broken variants.
///
/// <para>Every payload is small and self-contained (no external assets). Each is surfaced by a stable,
/// human-readable name through <c>[MemberData]</c> so a failing case clearly identifies which hostile
/// fixture broke the opener.</para>
/// </summary>
internal static class MalformedEpubCorpus
{
    /// <summary>Reference width of the deeply-nested HTML fixture (kept comfortably below the layout
    /// engine's recursion ceiling so a bounded-deep — but pathological — chapter still degrades
    /// gracefully rather than risking a stack overflow in the shared renderer).</summary>
    private const int DeepNestDepth = 128;

    /// <summary>Enumerates the full corpus as <c>(Name, Bytes)</c> pairs.</summary>
    /// <returns>Every malformed EPUB payload in the corpus.</returns>
    public static IEnumerable<(string Name, byte[] Bytes)> All()
    {
        yield return ("zero-byte", []);
        yield return ("garbage", Garbage());
        yield return ("foreign-magic-png", ForeignMagicPng());
        yield return ("truncated-zip", TruncatedZip());
        yield return ("empty-zip", EmptyZip());
        yield return ("no-mimetype", NoMimetype());
        yield return ("no-container-xml", NoContainerXml());
        yield return ("container-points-at-missing-opf", ContainerPointsAtMissingOpf());
        yield return ("malformed-opf-xml", MalformedOpfXml());
        yield return ("spine-itemref-missing-manifest-item", SpineItemRefMissingManifestItem());
        yield return ("chapter-html-pathologically-deep", ChapterPathologicallyDeep());
        yield return ("chapter-html-broken-unclosed", ChapterBrokenUnclosed());
    }

    // ── Non-zip payloads ──

    // A few KB of non-zip bytes (no PK signature anywhere).
    private static byte[] Garbage()
    {
        var bytes = new byte[4096];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(((i * 37) + 11) & 0x3F | 0x40); // 0x40..0x7F letters/symbols, never 'P'/'K' run.
        }

        return bytes;
    }

    // A real PNG signature + filler — a non-EPUB file masquerading by extension.
    private static byte[] ForeignMagicPng()
    {
        byte[] sig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        byte[] body = Encoding.ASCII.GetBytes("\0\0\0\rIHDR not-a-real-png body padding padding padding");
        var result = new byte[sig.Length + body.Length];
        sig.CopyTo(result, 0);
        body.CopyTo(result, sig.Length);
        return result;
    }

    // A valid EPUB zip chopped in half — central directory is gone.
    private static byte[] TruncatedZip()
    {
        byte[] full = ValidZip("<p>hello</p>");
        return full[..(full.Length / 2)];
    }

    // ── Zip-but-structurally-broken payloads ──

    private static byte[] EmptyZip() => BuildZip(_ => { });

    // META-INF/container.xml + chapters present, but no `mimetype` entry.
    private static byte[] NoMimetype() => BuildZip(z =>
    {
        AddContainer(z);
        AddOpf(z);
        AddNcx(z);
        AddChapter(z, "chapter1.xhtml", "<p>hi</p>");
    });

    // container.xml absent (mimetype + OPF present).
    private static byte[] NoContainerXml() => BuildZip(z =>
    {
        AddMimetype(z);
        AddOpf(z);
        AddNcx(z);
        AddChapter(z, "chapter1.xhtml", "<p>hi</p>");
    });

    // container.xml references an OPF that does not exist in the archive.
    private static byte[] ContainerPointsAtMissingOpf() => BuildZip(z =>
    {
        AddMimetype(z);
        AddContainer(z); // points at OEBPS/content.opf …which we never add.
    });

    // OPF present but its XML is malformed (unclosed tags / undeclared prefix).
    private static byte[] MalformedOpfXml() => BuildZip(z =>
    {
        AddMimetype(z);
        AddContainer(z);
        WriteText(z, "OEBPS/content.opf", "<package><metadata><dc:title>broken and unclosed");
    });

    // Well-formed OPF whose spine itemref points at a manifest id that is not declared.
    private static byte[] SpineItemRefMissingManifestItem() => BuildZip(z =>
    {
        AddMimetype(z);
        AddContainer(z);
        WriteText(z, "OEBPS/content.opf",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="2.0" unique-identifier="id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="id">urn:uuid:0</dc:identifier><dc:title>T</dc:title><dc:language>en</dc:language>
              </metadata>
              <manifest><item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml"/></manifest>
              <spine toc="ncx"><itemref idref="ghost"/></spine>
            </package>
            """);
        AddNcx(z);
    });

    // A structurally valid EPUB whose single chapter is pathologically deep (a long nested-block chain).
    // Kept bounded (DeepNestDepth) so the eager paginator degrades gracefully rather than overflowing
    // the shared layout engine's stack.
    private static byte[] ChapterPathologicallyDeep()
    {
        var sb = new StringBuilder(DeepNestDepth * 24);
        for (int i = 0; i < DeepNestDepth; i++)
        {
            sb.Append("<div><blockquote>");
        }

        sb.Append("deeply nested leaf text");
        for (int i = 0; i < DeepNestDepth; i++)
        {
            sb.Append("</blockquote></div>");
        }

        return ValidZip(sb.ToString());
    }

    // A structurally valid EPUB whose chapter HTML is badly broken (unclosed/overlapping tags).
    private static byte[] ChapterBrokenUnclosed() =>
        ValidZip("<p>unclosed <b>bold <i>and italic <span>broken <div>block in inline <ul><li>item");

    // ── builders ──

    private static byte[] ValidZip(string chapterBody) => BuildZip(z =>
    {
        AddMimetype(z);
        AddContainer(z);
        AddOpf(z);
        AddNcx(z);
        AddChapter(z, "chapter1.xhtml", chapterBody);
    });

    private static byte[] BuildZip(Action<ZipArchive> populate)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            populate(zip);
        }

        return ms.ToArray();
    }

    private static void AddMimetype(ZipArchive zip)
    {
        var mime = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using var sw = new StreamWriter(mime.Open(), Encoding.ASCII);
        sw.Write("application/epub+zip");
    }

    private static void AddContainer(ZipArchive zip) => WriteText(zip, "META-INF/container.xml",
        """
        <?xml version="1.0" encoding="utf-8"?>
        <container xmlns="urn:oasis:names:tc:opendocument:xmlns:container" version="1.0">
          <rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles>
        </container>
        """);

    private static void AddOpf(ZipArchive zip) => WriteText(zip, "OEBPS/content.opf",
        """
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://www.idpf.org/2007/opf" version="2.0" unique-identifier="id">
          <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
            <dc:identifier id="id">urn:uuid:0</dc:identifier><dc:title>T</dc:title><dc:language>en</dc:language>
          </metadata>
          <manifest>
            <item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml"/>
            <item id="c1" href="chapter1.xhtml" media-type="application/xhtml+xml"/>
          </manifest>
          <spine toc="ncx"><itemref idref="c1"/></spine>
        </package>
        """);

    private static void AddNcx(ZipArchive zip) => WriteText(zip, "OEBPS/toc.ncx",
        """
        <?xml version="1.0" encoding="utf-8"?>
        <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
          <head><meta name="dtb:uid" content="id"/></head>
          <docTitle><text>T</text></docTitle>
          <navMap><navPoint id="np1" playOrder="1"><navLabel><text>Start</text></navLabel><content src="chapter1.xhtml"/></navPoint></navMap>
        </ncx>
        """);

    private static void AddChapter(ZipArchive zip, string href, string body) => WriteText(zip, $"OEBPS/{href}",
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <!DOCTYPE html>
        <html xmlns="http://www.w3.org/1999/xhtml"><head><title>C</title></head><body>{body}</body></html>
        """);

    private static void WriteText(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName);
        using var sw = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        sw.Write(content);
    }
}
