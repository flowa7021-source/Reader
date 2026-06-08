using System.Globalization;
using System.Text;

namespace Foliant.Engines.Fb2.Tests;

/// <summary>
/// A hand-built corpus of <b>malformed / hostile</b> FB2 payloads used to prove the FB2 opener's
/// robustness contract: a corrupt, non-XML, wrong-schema, truncated or pathologically-deep FB2 must
/// complete <i>promptly</i> — either opening into a usable document or throwing a <b>tame, catchable</b>
/// exception — never a <see cref="StackOverflowException"/>, hang, OOM or process crash. Mirrors
/// <c>Foliant.Engines.Pdf.Tests.MalformedPdfCorpus</c> for the FB2 (FictionBook 2.0 XML) format,
/// generalising <see cref="Fb2TestFactory"/>'s valid builder into deliberately-broken variants.
///
/// <para>Every payload is small and self-contained. Each is surfaced by a stable, human-readable name
/// through <c>[MemberData]</c> so a failing case clearly identifies which hostile fixture broke the
/// opener.</para>
/// </summary>
internal static class MalformedFb2Corpus
{
    private const string Fb2Ns = "http://www.gribuser.ru/xml/fictionbook/2.0";

    /// <summary>Depth of the nested-<c>&lt;section&gt;</c> chain fixture (the prompt's "~200"); the FB2
    /// section walker recurses per nesting level, so this proves a deep — but bounded — chain flattens
    /// promptly without overflowing.</summary>
    private const int SectionChainDepth = 200;

    /// <summary>Enumerates the full corpus as <c>(Name, Bytes)</c> pairs (UTF-8 encoded text).</summary>
    /// <returns>Every malformed FB2 payload in the corpus.</returns>
    public static IEnumerable<(string Name, byte[] Bytes)> All()
    {
        yield return ("zero-byte", []);
        yield return ("not-xml-garbage", Utf8("This is plain text, certainly not XML at all."));
        yield return ("malformed-xml-unclosed-tags", Utf8("<?xml version=\"1.0\"?><FictionBook><body><section><p>oops"));
        yield return ("well-formed-wrong-root", Utf8("<?xml version=\"1.0\"?><root><child/></root>"));
        yield return ("well-formed-wrong-namespace", WrongNamespace());
        yield return ("truncated-mid-element", TruncatedMidElement());
        yield return ("deeply-nested-section-chain", DeeplyNestedSectionChain());
        yield return ("huge-paragraph-content", HugeParagraph());
        yield return ("empty-fictionbook", EmptyFictionBook());
        yield return ("xml-declaration-only", Utf8("<?xml version=\"1.0\" encoding=\"utf-8\"?>"));
    }

    private static byte[] Utf8(string s) => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(s);

    // Well-formed XML, correct local root name, but the WRONG namespace → opener must reject.
    private static byte[] WrongNamespace() => Utf8(
        """
        <?xml version="1.0" encoding="utf-8"?>
        <FictionBook xmlns="http://example.com/not-fb2">
          <body><section><p>text in the wrong namespace</p></section></body>
        </FictionBook>
        """);

    // A valid FB2 prologue chopped off partway through an element (no closing tags).
    private static byte[] TruncatedMidElement()
    {
        string full =
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <FictionBook xmlns="{Fb2Ns}">
              <description><title-info><book-title>Truncated</book-title><lang>en</lang></title-info></description>
              <body><section><title><p>Chapter</p></title><p>The quick brown fox jumps over the la
            """;
        return Utf8(full);
    }

    // A genuine (finite) but pathologically deep nested-<section> chain ending in a leaf paragraph.
    private static byte[] DeeplyNestedSectionChain()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"<?xml version=\"1.0\" encoding=\"utf-8\"?><FictionBook xmlns=\"{Fb2Ns}\">");
        sb.Append("<description><title-info><book-title>Deep</book-title><lang>en</lang></title-info></description>");
        sb.Append("<body>");
        for (int i = 0; i < SectionChainDepth; i++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<section><p>level {i}</p>");
        }

        for (int i = 0; i < SectionChainDepth; i++)
        {
            sb.Append("</section>");
        }

        sb.Append("</body></FictionBook>");
        return Utf8(sb.ToString());
    }

    // One <p> with a very large (but bounded) text payload — proves big content paginates, not hangs.
    private static byte[] HugeParagraph()
    {
        string para = string.Concat(Enumerable.Repeat("The quick brown fox jumps over the lazy dog. ", 5000));
        return Utf8(
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <FictionBook xmlns="{Fb2Ns}">
              <description><title-info><book-title>Huge</book-title><lang>en</lang></title-info></description>
              <body><section><title><p>Big</p></title><p>{para}</p></section></body>
            </FictionBook>
            """);
    }

    // A valid, correctly-namespaced FictionBook with no body content at all (opener yields one empty page).
    private static byte[] EmptyFictionBook() => Utf8(
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <FictionBook xmlns="{Fb2Ns}">
          <description><title-info><book-title>Empty</book-title><lang>en</lang></title-info></description>
        </FictionBook>
        """);
}
