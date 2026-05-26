using System.Globalization;
using System.Reflection;
using System.Text;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

string assetsDir = ResolveAssetsDir();
Directory.CreateDirectory(assetsDir);

WriteFile(assetsDir, "pdf-text-en-10p.pdf", BuildTextPdf(EnglishLines));
WriteFile(assetsDir, "pdf-text-ru-10p.pdf", BuildTextPdf(RussianLines));
WriteFile(assetsDir, "broken-truncated.pdf", BuildTruncated());
WriteFile(assetsDir, "broken-empty.pdf", []);
WriteFile(assetsDir, "broken-bad-xref.pdf", BuildBadXref());

Console.WriteLine($"Generated test assets into {assetsDir}");
return 0;

static void WriteFile(string dir, string name, byte[] bytes)
{
    string path = Path.Combine(dir, name);
    File.WriteAllBytes(path, bytes);
    Console.WriteLine($"  {name} ({bytes.Length} bytes)");
}

// Deterministic 10-page text PDF. IncludeDocumentInformation is disabled so no
// CreationDate/Producer metadata is emitted; PdfPig's output is otherwise a pure
// function of its inputs, so re-runs are byte-identical and git does not churn.
static byte[] BuildTextPdf(IReadOnlyList<string> lines)
{
    using var builder = new PdfDocumentBuilder { IncludeDocumentInformation = false };
    var font = builder.AddTrueTypeFont(LoadEmbeddedFont());

    const double width = 595;
    const double height = 842;
    for (int page = 1; page <= 10; page++)
    {
        var pageBuilder = builder.AddPage(width, height);
        double y = height - 72;
        pageBuilder.AddText(
            string.Format(CultureInfo.InvariantCulture, "Page {0} of 10", page),
            12,
            new PdfPoint(72, y),
            font);
        foreach (string line in lines)
        {
            y -= 18;
            pageBuilder.AddText(line, 12, new PdfPoint(72, y), font);
        }
    }

    return NormalizeFileId(builder.Build());
}

// PdfPig writes a random two-element /ID array into the trailer on every Build, which
// would churn git on re-runs. Replace both 32-hex identifiers with a fixed value of the
// same length so byte offsets (and thus startxref) stay valid.
static byte[] NormalizeFileId(byte[] pdf)
{
    var enc = Encoding.Latin1;
    string text = enc.GetString(pdf);
    int idStart = text.LastIndexOf("/ID", StringComparison.Ordinal);
    if (idStart < 0)
    {
        return pdf;
    }

    int open = text.IndexOf('[', idStart);
    int close = text.IndexOf(']', open + 1);
    if (open < 0 || close < 0)
    {
        return pdf;
    }

    const string fixedId = " <00000000000000000000000000000000><00000000000000000000000000000000>";
    if (close - open - 1 != fixedId.Length)
    {
        return pdf;
    }

    string normalized = string.Concat(text.AsSpan(0, open + 1), fixedId, text.AsSpan(close));
    return enc.GetBytes(normalized);
}

// A valid single-page PDF cut off mid-stream: header and objects survive but the
// xref/trailer are gone, so a conforming loader must reject it.
static byte[] BuildTruncated()
{
    byte[] full = BuildTextPdf(EnglishLines);
    return full[..(full.Length / 2)];
}

// Hand-rolled PDF whose startxref points at a bogus byte offset. The body is fine;
// only the cross-reference offset is corrupt.
static byte[] BuildBadXref()
{
    var enc = Encoding.Latin1;
    var sb = new StringBuilder();
    sb.Append("%PDF-1.4\n");
    sb.Append("1 0 obj\n<</Type/Catalog/Pages 2 0 R>>\nendobj\n");
    sb.Append("2 0 obj\n<</Type/Pages/Kids[3 0 R]/Count 1>>\nendobj\n");
    sb.Append("3 0 obj\n<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]>>\nendobj\n");

    int xrefStart = enc.GetByteCount(sb.ToString());
    sb.Append("xref\n0 4\n0000000000 65535 f \n");
    sb.Append("0000000009 00000 n \n");
    sb.Append("0000000058 00000 n \n");
    sb.Append("0000000110 00000 n \n");

    // startxref deliberately off by a large margin so the xref table cannot be found.
    int bogusOffset = xrefStart + 4096;
    sb.Append(CultureInfo.InvariantCulture, $"trailer\n<</Size 4/Root 1 0 R>>\nstartxref\n{bogusOffset}\n%%EOF\n");

    return enc.GetBytes(sb.ToString());
}

static ReadOnlyMemory<byte> LoadEmbeddedFont()
{
    var asm = Assembly.GetExecutingAssembly();
    string name = Array.Find(asm.GetManifestResourceNames(), n => n.EndsWith("DejaVuSans.ttf", StringComparison.Ordinal))
        ?? throw new InvalidOperationException("Embedded DejaVuSans.ttf resource not found.");
    using var stream = asm.GetManifestResourceStream(name)!;
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return ms.ToArray();
}

static string ResolveAssetsDir()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "Foliant.sln")))
        {
            return Path.Combine(dir.FullName, "tests", "assets");
        }

        dir = dir.Parent;
    }

    throw new InvalidOperationException("Could not locate repo root (Foliant.sln) from " + AppContext.BaseDirectory);
}

internal static partial class Program
{
    private static readonly string[] EnglishLines =
    [
        "The quick brown fox jumps over the lazy dog.",
        "Foliant golden test asset: synthetic English text.",
        "This file is generated and licensed CC0.",
    ];

    private static readonly string[] RussianLines =
    [
        "Съешь же ещё этих мягких французских булок да выпей чаю.",
        "Фолиант: синтетический эталонный текст на кириллице.",
        "Этот файл сгенерирован и лицензирован по CC0.",
    ];
}
