using System.Diagnostics;
using System.Globalization;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Robustness/security tests for the cos-tree depth guard (<see cref="PdfCosLimits"/>): hand-built
/// <b>malformed</b> PDFs whose <c>/Kids</c> trees cycle (page tree and name tree) must not crash the
/// production services. Without the depth cap each recursive <c>/Kids</c> walker would recurse unbounded
/// on a cycle and throw an <b>uncatchable</b> <see cref="StackOverflowException"/> (crashing the test
/// process); with the cap the best-effort service returns a (possibly empty) list promptly. The test
/// merely <i>completing</i> is the core proof there is no stack overflow; we additionally assert it
/// returns a non-null list within a generous timeout.
///
/// Fixture approach: a <b>true cycle</b> (A→B→A), not deep nesting. Both fixtures give PdfPig a valid
/// catalog + one real leaf page so <c>PdfDocument.Open</c> succeeds (PdfPig counts pages off the leaf and
/// does <i>not</i> eagerly traverse the name tree); the cycle lives in a side branch that only our own
/// <c>/Kids</c> walkers follow — which is exactly the path the guard protects.
/// </summary>
public sealed class PdfCosCycleGuardTests : IDisposable
{
    // Generous ceiling: a guarded walk caps at PdfCosLimits.MaxTreeDepth (64) and returns in microseconds;
    // an unbounded one stack-overflows long before this. The bound only guards against an accidental hang.
    private static readonly TimeSpan CompletionBudget = TimeSpan.FromSeconds(30);

    private readonly string _tmpDir;

    public PdfCosCycleGuardTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-coscycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tmpDir, recursive: true);
        }
        catch
        {
            /* best-effort */
        }
    }

    [Fact]
    public async Task NamedDestinationService_CyclicPageTree_CompletesWithoutStackOverflow()
    {
        // PdfNamedDestinationCosReader.Read builds a pageRef→index map by walking Catalog → /Pages → /Kids
        // (its WalkPages). The fixture's page tree has a real leaf (so PdfPig opens it) PLUS a self-cyclic
        // intermediate /Pages node; the unguarded WalkPages would recurse on that cycle forever.
        string path = await WriteFixtureAsync("cyclic-pages.pdf", CyclicPageTreePdf());
        var service = new PdfPigNamedDestinationService(NullLogger<PdfPigNamedDestinationService>.Instance);

        var sw = Stopwatch.StartNew();
        var list = await service.ListAsync(path, default);
        sw.Stop();

        list.Should().NotBeNull("best-effort service returns a (possibly empty) list, never crashes");
        sw.Elapsed.Should().BeLessThan(CompletionBudget, "the depth cap must make the walk terminate promptly");
    }

    [Fact]
    public async Task AttachmentService_CyclicNameTree_CompletesWithoutStackOverflow()
    {
        // PdfAttachmentCosReader.Read walks Catalog → /Names → /EmbeddedFiles → /Kids (its Walk). PdfPig
        // does not eagerly traverse the name tree, so a cyclic /EmbeddedFiles tree only bites our walker;
        // the unguarded Walk would recurse on the A→B→A cycle forever.
        string path = await WriteFixtureAsync("cyclic-names.pdf", CyclicEmbeddedFilesNameTreePdf());
        var service = new PdfPigAttachmentService(NullLogger<PdfPigAttachmentService>.Instance);

        var sw = Stopwatch.StartNew();
        var list = await service.ListAsync(path, default);
        sw.Stop();

        list.Should().NotBeNull("best-effort service returns a (possibly empty) list, never crashes");
        sw.Elapsed.Should().BeLessThan(CompletionBudget, "the depth cap must make the walk terminate promptly");
    }

    private async Task<string> WriteFixtureAsync(string name, byte[] bytes)
    {
        string path = Path.Combine(_tmpDir, name);
        await File.WriteAllBytesAsync(path, bytes, default);
        return path;
    }

    /// <summary>
    /// Malformed PDF whose page tree cycles. Objects:
    /// 1 = Catalog (/Pages 2 0 R), 2 = root /Pages with one real leaf (3) AND a cyclic intermediate (4),
    /// 3 = a valid /Type /Page (lets PdfPig open the doc and report a page), 4 = a /Type /Pages whose
    /// /Kids references itself (4 0 R) — the cycle our WalkPages must not follow forever.
    /// </summary>
    private static byte[] CyclicPageTreePdf()
    {
        var objects = new List<(int ObjNumber, string Body)>
        {
            (1, "1 0 obj\n<</Type/Catalog/Pages 2 0 R>>\nendobj\n"),

            // Root node advertises a single leaf via /Count 1; /Kids holds the real leaf plus a poisoned
            // intermediate node. PdfPig resolves pages off the leaf; our walker also descends node 4.
            (2, "2 0 obj\n<</Type/Pages/Kids[3 0 R 4 0 R]/Count 1>>\nendobj\n"),

            (3, "3 0 obj\n<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]>>\nendobj\n"),

            // Self-referential intermediate page-tree node: /Kids points back at itself → A→A cycle.
            (4, "4 0 obj\n<</Type/Pages/Parent 2 0 R/Kids[4 0 R]/Count 1>>\nendobj\n"),
        };

        return Assemble(objects, rootObj: 1);
    }

    /// <summary>
    /// Malformed PDF whose <c>/EmbeddedFiles</c> name tree cycles. Objects:
    /// 1 = Catalog (/Pages 2 0 R, /Names 5 0 R), 2 = /Pages with one leaf (3), 3 = valid /Page,
    /// 5 = /Names dict (/EmbeddedFiles 6 0 R), 6 = name-tree node A (/Kids [7 0 R]),
    /// 7 = name-tree node B (/Kids [6 0 R]) → A→B→A cycle the attachment Walk must not follow forever.
    /// </summary>
    private static byte[] CyclicEmbeddedFilesNameTreePdf()
    {
        var objects = new List<(int ObjNumber, string Body)>
        {
            (1, "1 0 obj\n<</Type/Catalog/Pages 2 0 R/Names 5 0 R>>\nendobj\n"),
            (2, "2 0 obj\n<</Type/Pages/Kids[3 0 R]/Count 1>>\nendobj\n"),
            (3, "3 0 obj\n<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]>>\nendobj\n"),

            // /Names → /EmbeddedFiles points at name-tree node A (obj 6).
            (5, "5 0 obj\n<</EmbeddedFiles 6 0 R>>\nendobj\n"),

            // Node A → Node B → Node A: a two-node /Kids cycle.
            (6, "6 0 obj\n<</Kids[7 0 R]>>\nendobj\n"),
            (7, "7 0 obj\n<</Kids[6 0 R]>>\nendobj\n"),
        };

        return Assemble(objects, rootObj: 1);
    }

    /// <summary>
    /// Serialises sparse, hand-numbered objects into a PDF with a classic cross-reference table and
    /// trailer (mirrors <see cref="LegacyDestsPdfFactory"/>). Object numbers may be non-contiguous; gaps
    /// are emitted as free ("f") xref entries so PdfPig still parses the table.
    /// </summary>
    private static byte[] Assemble(IReadOnlyList<(int ObjNumber, string Body)> objects, int rootObj)
    {
        var enc = Encoding.Latin1;
        int maxObj = 0;
        foreach (var (objNumber, _) in objects)
        {
            maxObj = Math.Max(maxObj, objNumber);
        }

        var offsets = new int[maxObj + 1]; // 1-based object numbers; 0 stays unused (free head)
        var present = new bool[maxObj + 1];

        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");
        foreach (var (objNumber, body) in objects)
        {
            offsets[objNumber] = enc.GetByteCount(sb.ToString());
            present[objNumber] = true;
            sb.Append(body);
        }

        int xrefStart = enc.GetByteCount(sb.ToString());
        AppendXref(sb, offsets, present, maxObj);
        sb.Append(CultureInfo.InvariantCulture,
            $"trailer\n<</Size {maxObj + 1}/Root {rootObj} 0 R>>\nstartxref\n{xrefStart}\n%%EOF\n");

        return enc.GetBytes(sb.ToString());
    }

    private static void AppendXref(StringBuilder sb, int[] offsets, bool[] present, int maxObj)
    {
        sb.Append("xref\n");
        sb.Append(CultureInfo.InvariantCulture, $"0 {maxObj + 1}\n");
        sb.Append("0000000000 65535 f \n"); // object 0 — free list head
        for (int obj = 1; obj <= maxObj; obj++)
        {
            sb.Append(present[obj]
                ? string.Create(CultureInfo.InvariantCulture, $"{offsets[obj]:D10} 00000 n \n")
                : "0000000000 00000 f \n"); // gap → free entry, keeps the subsection contiguous
        }
    }
}
