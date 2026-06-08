using System.Globalization;
using System.Text;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// A hand-built corpus of <b>malformed / hostile</b> PDF payloads used to prove the documented
/// best-effort contract of every PdfPig-based read/inspect service: a corrupt, truncated, cyclic or
/// otherwise hostile PDF must yield an <i>empty-or-partial</i> result <b>promptly</b> — never a crash,
/// hang, or unhandled throw. A malicious PDF must never be able to take down the reader.
///
/// <para>
/// Every payload is small and self-contained (no external assets). Cases that need a valid xref/trailer
/// reuse the classic <see cref="Assemble"/> serializer (mirrors <c>PdfCosCycleGuardTests.Assemble</c> and
/// <see cref="LegacyDestsPdfFactory"/>): sparse, hand-numbered objects → header + bodies + cross-reference
/// table + trailer. The two cycle fixtures (<see cref="CyclicPageTree"/>, <see cref="CyclicNameTree"/>)
/// generalise the original cycle-guard fixtures so the broad robustness matrix covers them too.
/// </para>
/// </summary>
internal static class MalformedPdfCorpus
{
    private static readonly Encoding Enc = Encoding.Latin1;

    /// <summary>
    /// Enumerates the full corpus as <c>(Name, Bytes)</c> pairs. The name is a stable, human-readable id
    /// surfaced in <c>[MemberData]</c> so a failing matrix cell clearly identifies <i>which</i> hostile
    /// fixture broke <i>which</i> service.
    /// </summary>
    /// <returns>Every malformed payload in the corpus.</returns>
    public static IEnumerable<(string Name, byte[] Bytes)> All()
    {
        yield return ("empty", Empty());
        yield return ("garbage", Garbage());
        yield return ("header-only", HeaderOnly());
        yield return ("truncated-mid-object", TruncatedMidObject());
        yield return ("truncated-before-xref", TruncatedBeforeXref());
        yield return ("missing-trailer", MissingTrailer());
        yield return ("bad-xref-offsets", BadXrefOffsets());
        yield return ("dangling-indirect-ref", DanglingIndirectRef());
        yield return ("catalog-missing-pages", CatalogMissingPages());
        yield return ("lying-stream-length", LyingStreamLength());
        yield return ("cyclic-page-tree", CyclicPageTree());
        yield return ("cyclic-name-tree", CyclicNameTree());
        yield return ("deeply-nested", DeeplyNested());
        yield return ("foreign-magic-png", ForeignMagicPng());
        yield return ("valid-no-pages", ValidNoPages());
    }

    // 1. Empty: 0 bytes. PdfPig.Open must reject an empty buffer; services must degrade, not throw out.
    private static byte[] Empty() => [];

    // 2. Garbage: a few KB of non-PDF bytes with no "%PDF" header anywhere.
    private static byte[] Garbage()
    {
        var bytes = new byte[4096];
        // Deterministic non-zero filler that never spells "%PDF" (avoids byte 0x25 '%' followed by "PDF").
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(((i * 37) + 11) & 0x7F | 0x40); // 0x40..0x7F range: letters/symbols, no '%'.
        }

        return bytes;
    }

    // 3. Header only: a valid PDF header line and then nothing — no body, xref, or trailer.
    private static byte[] HeaderOnly() => Enc.GetBytes("%PDF-1.7\n");

    // 4. Truncated mid-object: a valid Assemble output chopped partway through the object body, so an
    //    object dictionary is cut off mid-token and the xref/trailer are gone.
    private static byte[] TruncatedMidObject()
    {
        byte[] full = Assemble(MinimalObjects(), rootObj: 1);
        // Cut just past the header + first object so we land inside object 2's dictionary.
        int cut = Enc.GetByteCount("%PDF-1.4\n1 0 obj\n<</Type/Catalog/Pages 2 0 R>>\nendobj\n2 0 obj\n<</Type");
        cut = Math.Min(cut, full.Length);
        return full[..cut];
    }

    // 5. Truncated before xref: all objects present and well-formed, but everything from "startxref"
    //    (and the xref table + trailer) onward is chopped off.
    private static byte[] TruncatedBeforeXref()
    {
        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");
        foreach (var (_, body) in MinimalObjects())
        {
            sb.Append(body);
        }

        // Note the deliberate absence of xref/trailer/startxref/%%EOF.
        return Enc.GetBytes(sb.ToString());
    }

    // 6. Missing trailer: objects + a real xref table are present, but the "trailer"/"startxref" block
    //    is absent, so there is no Root pointer to anchor parsing.
    private static byte[] MissingTrailer()
    {
        var objects = MinimalObjects();
        int maxObj = MaxObjNumber(objects);

        var offsets = new int[maxObj + 1];
        var present = new bool[maxObj + 1];

        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");
        foreach (var (objNumber, body) in objects)
        {
            offsets[objNumber] = Enc.GetByteCount(sb.ToString());
            present[objNumber] = true;
            sb.Append(body);
        }

        AppendXref(sb, offsets, present, maxObj);
        sb.Append("%%EOF\n"); // EOF but no trailer/startxref.
        return Enc.GetBytes(sb.ToString());
    }

    // 7. Bad xref offsets: a well-formed trailer/xref shape, but every "n" entry points to a wrong byte
    //    offset (shifted), so a parser that trusts the table seeks into the middle of objects.
    private static byte[] BadXrefOffsets()
    {
        var objects = MinimalObjects();
        int maxObj = MaxObjNumber(objects);
        var present = new bool[maxObj + 1];

        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");
        foreach (var (objNumber, body) in objects)
        {
            present[objNumber] = true;
            sb.Append(body);
        }

        int xrefStart = Enc.GetByteCount(sb.ToString());

        // Bogus offsets: a fixed wrong value for every present object (points mid-stream, not at "N 0 obj").
        var badOffsets = new int[maxObj + 1];
        for (int i = 0; i <= maxObj; i++)
        {
            badOffsets[i] = 7; // inside the "%PDF-1.4" header — never the start of an object.
        }

        AppendXref(sb, badOffsets, present, maxObj);
        sb.Append(CultureInfo.InvariantCulture,
            $"trailer\n<</Size {maxObj + 1}/Root 1 0 R>>\nstartxref\n{xrefStart}\n%%EOF\n");
        return Enc.GetBytes(sb.ToString());
    }

    // 8. Dangling indirect refs: a structurally valid file whose Catalog points /Pages and /Names at
    //    object 999, which does not exist. Resolvers must treat the missing object as absent, not throw.
    private static byte[] DanglingIndirectRef()
    {
        var objects = new List<(int, string)>
        {
            (1, "1 0 obj\n<</Type/Catalog/Pages 999 0 R/Names 999 0 R>>\nendobj\n"),
            // A real page tree + leaf so PdfPig can still open the document.
            (2, "2 0 obj\n<</Type/Pages/Kids[3 0 R]/Count 1>>\nendobj\n"),
            (3, "3 0 obj\n<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]>>\nendobj\n"),
        };

        return Assemble(objects, rootObj: 1);
    }

    // 9. Catalog missing /Pages: a Catalog dictionary with no /Pages entry at all (and no page tree).
    private static byte[] CatalogMissingPages()
    {
        var objects = new List<(int, string)>
        {
            (1, "1 0 obj\n<</Type/Catalog>>\nendobj\n"),
        };

        return Assemble(objects, rootObj: 1);
    }

    // 10. Lying stream /Length: a content stream whose /Length (10000) vastly exceeds the few real bytes
    //     and runs past EOF. A naive reader that trusts /Length reads out of bounds.
    private static byte[] LyingStreamLength()
    {
        var objects = new List<(int, string)>
        {
            (1, "1 0 obj\n<</Type/Catalog/Pages 2 0 R>>\nendobj\n"),
            (2, "2 0 obj\n<</Type/Pages/Kids[3 0 R]/Count 1>>\nendobj\n"),
            (3, "3 0 obj\n<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]/Contents 4 0 R>>\nendobj\n"),
            // /Length lies: claims 10000 bytes but only a handful follow before endstream/EOF.
            (4, "4 0 obj\n<</Length 10000>>\nstream\nBT /F1 12 Tf (hi) Tj ET\nendstream\nendobj\n"),
        };

        return Assemble(objects, rootObj: 1);
    }

    /// <summary>
    /// 11a. Malformed PDF whose <b>page tree cycles</b> (generalised from the original cycle-guard
    /// fixture): a valid Catalog + real leaf page (so PdfPig opens it) plus a self-referential
    /// intermediate <c>/Pages</c> node (<c>/Kids [4 0 R]</c> on object 4) that only our own <c>/Kids</c>
    /// walkers descend — exactly the branch the depth guard protects.
    /// </summary>
    private static byte[] CyclicPageTree()
    {
        var objects = new List<(int, string)>
        {
            (1, "1 0 obj\n<</Type/Catalog/Pages 2 0 R>>\nendobj\n"),
            (2, "2 0 obj\n<</Type/Pages/Kids[3 0 R 4 0 R]/Count 1>>\nendobj\n"),
            (3, "3 0 obj\n<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]>>\nendobj\n"),
            (4, "4 0 obj\n<</Type/Pages/Parent 2 0 R/Kids[4 0 R]/Count 1>>\nendobj\n"),
        };

        return Assemble(objects, rootObj: 1);
    }

    /// <summary>
    /// 11b. Malformed PDF whose <c>/EmbeddedFiles</c> <b>name tree cycles</b> A→B→A (generalised from the
    /// original cycle-guard fixture): valid Catalog + leaf page; <c>/Names → /EmbeddedFiles</c> points at
    /// name-tree node 6 whose <c>/Kids</c> is [7], whose <c>/Kids</c> is [6]. PdfPig does not eagerly walk
    /// the name tree, so the cycle only bites our attachment/destination walkers.
    /// </summary>
    private static byte[] CyclicNameTree()
    {
        var objects = new List<(int, string)>
        {
            (1, "1 0 obj\n<</Type/Catalog/Pages 2 0 R/Names 5 0 R>>\nendobj\n"),
            (2, "2 0 obj\n<</Type/Pages/Kids[3 0 R]/Count 1>>\nendobj\n"),
            (3, "3 0 obj\n<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]>>\nendobj\n"),
            (5, "5 0 obj\n<</EmbeddedFiles 6 0 R>>\nendobj\n"),
            (6, "6 0 obj\n<</Kids[7 0 R]>>\nendobj\n"),
            (7, "7 0 obj\n<</Kids[6 0 R]>>\nendobj\n"),
        };

        return Assemble(objects, rootObj: 1);
    }

    /// <summary>
    /// 12. Non-cyclic but <b>pathologically deep</b> nesting: a page tree whose intermediate <c>/Pages</c>
    /// nodes chain ~200 levels deep — far past <see cref="PdfCosLimits.MaxTreeDepth"/> (64) — ending in a
    /// real leaf. Unlike the cycle fixtures this is a genuine (finite) tree, so it exercises the depth cap
    /// on legitimately deep input: the guard must make the recursive walk terminate fast, not stack-overflow.
    /// </summary>
    private static byte[] DeeplyNested()
    {
        const int depth = 200;

        var objects = new List<(int, string)>
        {
            (1, "1 0 obj\n<</Type/Catalog/Pages 2 0 R>>\nendobj\n"),
        };

        // Object 2 is the root /Pages; objects 2..(depth+1) are intermediate /Pages nodes, each pointing
        // at the next; the final node holds the single real leaf page (object depth+2).
        int leafObj = depth + 2;
        for (int level = 0; level < depth; level++)
        {
            int objNumber = 2 + level;
            int childObj = objNumber + 1; // next /Pages node, or the leaf for the last level.
            string parent = level == 0 ? string.Empty : $"/Parent {objNumber - 1} 0 R";
            objects.Add((objNumber,
                string.Create(CultureInfo.InvariantCulture,
                    $"{objNumber} 0 obj\n<</Type/Pages{parent}/Kids[{childObj} 0 R]/Count 1>>\nendobj\n")));
        }

        objects.Add((leafObj,
            string.Create(CultureInfo.InvariantCulture,
                $"{leafObj} 0 obj\n<</Type/Page/Parent {leafObj - 1} 0 R/MediaBox[0 0 595 842]>>\nendobj\n")));

        return Assemble(objects, rootObj: 1);
    }

    // 13. Foreign magic: a real PNG signature (and a bit of filler) — a non-PDF file masquerading by
    //     extension. The header sniffers must reject it without throwing.
    private static byte[] ForeignMagicPng()
    {
        byte[] pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        byte[] ihdr = Enc.GetBytes("\0\0\0\rIHDR followed by not-a-real-png body");
        var result = new byte[pngSignature.Length + ihdr.Length];
        pngSignature.CopyTo(result, 0);
        ihdr.CopyTo(result, pngSignature.Length);
        return result;
    }

    // 14. Valid-ish but no pages: a structurally sound Catalog whose /Pages has an empty /Kids and
    //     /Count 0 — opens fine but has zero pages; readers must return empty/partial, not throw.
    private static byte[] ValidNoPages()
    {
        var objects = new List<(int, string)>
        {
            (1, "1 0 obj\n<</Type/Catalog/Pages 2 0 R>>\nendobj\n"),
            (2, "2 0 obj\n<</Type/Pages/Kids[]/Count 0>>\nendobj\n"),
        };

        return Assemble(objects, rootObj: 1);
    }

    /// <summary>The minimal valid object set (Catalog → Pages → one leaf Page) reused by truncation cases.</summary>
    private static List<(int ObjNumber, string Body)> MinimalObjects() =>
    [
        (1, "1 0 obj\n<</Type/Catalog/Pages 2 0 R>>\nendobj\n"),
        (2, "2 0 obj\n<</Type/Pages/Kids[3 0 R]/Count 1>>\nendobj\n"),
        (3, "3 0 obj\n<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]>>\nendobj\n"),
    ];

    private static int MaxObjNumber(IReadOnlyList<(int ObjNumber, string Body)> objects)
    {
        int maxObj = 0;
        foreach (var (objNumber, _) in objects)
        {
            maxObj = Math.Max(maxObj, objNumber);
        }

        return maxObj;
    }

    /// <summary>
    /// Serialises sparse, hand-numbered objects into a PDF with a classic cross-reference table and
    /// trailer (mirrors <c>PdfCosCycleGuardTests.Assemble</c> / <see cref="LegacyDestsPdfFactory"/>).
    /// Object numbers may be non-contiguous; gaps are emitted as free ("f") xref entries so the table
    /// stays a contiguous subsection.
    /// </summary>
    /// <param name="objects">Hand-numbered object bodies (each a complete <c>N 0 obj … endobj</c>).</param>
    /// <param name="rootObj">Object number of the document Catalog, written into the trailer <c>/Root</c>.</param>
    /// <returns>The assembled PDF bytes.</returns>
    private static byte[] Assemble(IReadOnlyList<(int ObjNumber, string Body)> objects, int rootObj)
    {
        int maxObj = MaxObjNumber(objects);

        var offsets = new int[maxObj + 1]; // 1-based object numbers; 0 stays unused (free head)
        var present = new bool[maxObj + 1];

        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");
        foreach (var (objNumber, body) in objects)
        {
            offsets[objNumber] = Enc.GetByteCount(sb.ToString());
            present[objNumber] = true;
            sb.Append(body);
        }

        int xrefStart = Enc.GetByteCount(sb.ToString());
        AppendXref(sb, offsets, present, maxObj);
        sb.Append(CultureInfo.InvariantCulture,
            $"trailer\n<</Size {maxObj + 1}/Root {rootObj} 0 R>>\nstartxref\n{xrefStart}\n%%EOF\n");

        return Enc.GetBytes(sb.ToString());
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
