using System.IO.Compression;
using System.Text;

namespace Foliant.Engines.Epub.Tests;

/// <summary>
/// Hand-builds a minimal valid EPUB 2 archive for unit tests. Layout:
/// <list type="bullet">
/// <item><c>mimetype</c> (STORED, не compressed; EPUB spec требует это первым в архиве).</item>
/// <item><c>META-INF/container.xml</c> — указывает на OPF.</item>
/// <item><c>OEBPS/content.opf</c> — manifest + spine.</item>
/// <item><c>OEBPS/chapter*.xhtml</c> — по одному на spine item.</item>
/// <item><c>OEBPS/toc.ncx</c> — EPUB 2 TOC (минимальный).</item>
/// </list>
/// VersOne.Epub читает такие архивы без претензий.
/// </summary>
internal static class EpubTestFactory
{
    /// <summary>Создаёт EPUB файл с указанными chapter'ами (HTML контент per chapter).
    /// Возвращает путь к новому файлу.</summary>
    public static string Create(string targetDir, string title, string author, params string[] chapterHtmls) =>
        Create(targetDir, title, author, images: null, css: null, chapterHtmls);

    /// <summary>Создаёт EPUB с одним linked-stylesheet'ом (manifest item <c>text/css</c> + файл
    /// <c>OEBPS/{cssHref}</c>), на который каждая глава ссылается через <c>&lt;link rel="stylesheet"&gt;</c>
    /// в <c>&lt;head&gt;</c>. Для проверки author-CSS каскада сквозь EPUB-резолвер.</summary>
    /// <param name="targetDir">Каталог, в котором создаётся файл.</param>
    /// <param name="title">Title метаданных.</param>
    /// <param name="author">Author метаданных.</param>
    /// <param name="cssHref">Путь CSS-файла относительно <c>OEBPS/</c> (например <c>styles/main.css</c>).</param>
    /// <param name="cssBody">Содержимое CSS-файла.</param>
    /// <param name="chapterHtmls">HTML-контент каждого spine-item (тело <c>&lt;body&gt;</c>).</param>
    /// <returns>Путь к новому <c>.epub</c>.</returns>
    public static string CreateWithCss(string targetDir, string title, string author, string cssHref, string cssBody, params string[] chapterHtmls) =>
        Create(targetDir, title, author, images: null, css: new EpubCss(cssHref, cssBody), chapterHtmls);

    /// <summary>Создаёт EPUB файл с указанными chapter'ами и (опционально) встроенными raster
    /// image'ами. Каждый image добавляется как <c>OEBPS/{href}</c> с соответствующим manifest-item;
    /// chapter'ы могут ссылаться на него через chapter-relative <c>&lt;img src&gt;</c>.</summary>
    /// <param name="targetDir">Каталог, в котором создаётся файл.</param>
    /// <param name="title">Title метаданных.</param>
    /// <param name="author">Author метаданных.</param>
    /// <param name="images">Встраиваемые ресурсы (href относительно <c>OEBPS/</c>, media-type, bytes),
    /// либо <see langword="null"/>.</param>
    /// <param name="chapterHtmls">HTML-контент каждого spine-item.</param>
    /// <returns>Путь к новому <c>.epub</c>.</returns>
    public static string Create(string targetDir, string title, string author, IReadOnlyList<EpubImage>? images, params string[] chapterHtmls) =>
        Create(targetDir, title, author, images, css: null, chapterHtmls);

    private static string Create(string targetDir, string title, string author, IReadOnlyList<EpubImage>? images, EpubCss? css, params string[] chapterHtmls)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Directory.CreateDirectory(targetDir);
        string path = Path.Combine(targetDir, $"book-{Guid.NewGuid():N}.epub");

        using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);

        // mimetype: первый, без компрессии. EPUB spec.
        var mime = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var sw = new StreamWriter(mime.Open(), Encoding.ASCII))
        {
            sw.Write("application/epub+zip");
        }

        WriteText(zip, "META-INF/container.xml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <container xmlns="urn:oasis:names:tc:opendocument:xmlns:container" version="1.0">
              <rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles>
            </container>
            """);

        string cssLink = css is null
            ? string.Empty
            : $"<link rel=\"stylesheet\" type=\"text/css\" href=\"{css.Href}\"/>";

        var manifestItems = new StringBuilder();
        var spineItems = new StringBuilder();
        for (int i = 0; i < chapterHtmls.Length; i++)
        {
            string id = $"ch{i + 1}";
            string href = $"chapter{i + 1}.xhtml";
            manifestItems.Append(System.Globalization.CultureInfo.InvariantCulture, $"<item id=\"{id}\" href=\"{href}\" media-type=\"application/xhtml+xml\"/>\n");
            spineItems.Append(System.Globalization.CultureInfo.InvariantCulture, $"<itemref idref=\"{id}\"/>\n");
            WriteText(zip, $"OEBPS/{href}",
                $"""
                <?xml version="1.0" encoding="utf-8"?>
                <!DOCTYPE html>
                <html xmlns="http://www.w3.org/1999/xhtml">
                  <head><title>Chapter {i + 1}</title>{cssLink}</head>
                  <body>{chapterHtmls[i]}</body>
                </html>
                """);
        }

        if (css is not null)
        {
            manifestItems.Append(System.Globalization.CultureInfo.InvariantCulture, $"<item id=\"css1\" href=\"{css.Href}\" media-type=\"text/css\"/>\n");
            WriteText(zip, $"OEBPS/{css.Href}", css.Body);
        }

        if (images is not null)
        {
            for (int i = 0; i < images.Count; i++)
            {
                EpubImage img = images[i];
                manifestItems.Append(System.Globalization.CultureInfo.InvariantCulture, $"<item id=\"img{i + 1}\" href=\"{img.Href}\" media-type=\"{img.MediaType}\"/>\n");
                WriteBytes(zip, $"OEBPS/{img.Href}", img.Bytes);
            }
        }

        WriteText(zip, "OEBPS/content.opf",
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="2.0" unique-identifier="book-id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:opf="http://www.idpf.org/2007/opf">
                <dc:identifier id="book-id" opf:scheme="UUID">urn:uuid:{Guid.NewGuid()}</dc:identifier>
                <dc:title>{title}</dc:title>
                <dc:creator opf:role="aut">{author}</dc:creator>
                <dc:language>en</dc:language>
              </metadata>
              <manifest>
                <item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml"/>
                {manifestItems}
              </manifest>
              <spine toc="ncx">
                {spineItems}
              </spine>
            </package>
            """);

        WriteText(zip, "OEBPS/toc.ncx",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
              <head><meta name="dtb:uid" content="book-id"/></head>
              <docTitle><text>Title</text></docTitle>
              <navMap><navPoint id="np-1" playOrder="1"><navLabel><text>Start</text></navLabel><content src="chapter1.xhtml"/></navPoint></navMap>
            </ncx>
            """);

        return path;
    }

    private static void WriteText(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName);
        using var sw = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        sw.Write(content);
    }

    private static void WriteBytes(ZipArchive zip, string entryName, byte[] content)
    {
        var entry = zip.CreateEntry(entryName);
        using Stream s = entry.Open();
        s.Write(content, 0, content.Length);
    }
}

/// <summary>An embedded raster resource for <see cref="EpubTestFactory"/>: its <c>href</c> relative
/// to <c>OEBPS/</c>, its OPF media-type, and the raw encoded bytes.</summary>
/// <param name="Href">Path relative to <c>OEBPS/</c> (e.g. <c>img/pic.png</c>).</param>
/// <param name="MediaType">OPF manifest media-type (e.g. <c>image/png</c>).</param>
/// <param name="Bytes">Encoded image bytes.</param>
internal sealed record EpubImage(string Href, string MediaType, byte[] Bytes);

/// <summary>A linked stylesheet for <see cref="EpubTestFactory"/>: its <c>href</c> relative to
/// <c>OEBPS/</c> and its CSS body.</summary>
/// <param name="Href">Path relative to <c>OEBPS/</c> (e.g. <c>styles/main.css</c>).</param>
/// <param name="Body">CSS text.</param>
internal sealed record EpubCss(string Href, string Body);
