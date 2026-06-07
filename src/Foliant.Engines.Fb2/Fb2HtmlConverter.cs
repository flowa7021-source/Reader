using System.Text;
using System.Xml.Linq;

namespace Foliant.Engines.Fb2;

/// <summary>
/// Converts the <em>direct</em> (non-nested) content of one FB2 <c>&lt;section&gt;</c> (or a
/// section-less <c>&lt;body&gt;</c>) into an HTML fragment for <c>Foliant.Rendering.Html</c>.
///
/// <para>This is a structural mapping for the renderer, not a fidelity-preserving FB2→XHTML
/// transform: it emits headings, paragraphs and block-quotes so the layout engine can word-wrap and
/// paginate. Block mapping: <c>&lt;title&gt;</c>→<c>&lt;h2&gt;</c> (its <c>&lt;p&gt;</c> lines joined
/// by <c>&lt;br/&gt;</c>), <c>&lt;subtitle&gt;</c>→<c>&lt;h3&gt;</c>, <c>&lt;p&gt;</c>→<c>&lt;p&gt;</c>,
/// <c>&lt;epigraph&gt;</c>/<c>&lt;cite&gt;</c>→<c>&lt;blockquote&gt;</c>, <c>&lt;empty-line/&gt;</c>→
/// <c>&lt;br/&gt;</c>. Inline mapping: <c>&lt;emphasis&gt;</c>→<c>&lt;em&gt;</c>,
/// <c>&lt;strong&gt;</c>→<c>&lt;strong&gt;</c>, <c>&lt;strikethrough&gt;</c>→<c>&lt;s&gt;</c>,
/// <c>&lt;sub&gt;</c>/<c>&lt;sup&gt;</c> pass through, <c>&lt;a&gt;</c>→plain text (href dropped).
/// Unknown elements recurse into their children, emitting their text. All text content is
/// HTML-encoded (<c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>).</para>
///
/// <para>The converter is pure (<see cref="XElement"/>→<see cref="string"/>, no I/O) so it can be
/// unit-tested in isolation. Nested <c>&lt;section&gt;</c> elements are <em>not</em> descended into —
/// the caller flattens them into their own chapters.</para>
/// </summary>
internal static class Fb2HtmlConverter
{
    /// <summary>FB2 namespace URI shared with <see cref="Fb2Document"/>.</summary>
    private static readonly XNamespace Fb2Ns = "http://www.gribuser.ru/xml/fictionbook/2.0";

    /// <summary>Converts the direct block-level children of a section (or section-less body) into an
    /// HTML fragment. Nested <c>&lt;section&gt;</c> children are skipped (flattened by the caller).</summary>
    /// <param name="section">The FB2 <c>&lt;section&gt;</c> or <c>&lt;body&gt;</c> element.</param>
    /// <returns>An HTML fragment (possibly empty if the section has no direct renderable content).</returns>
    public static string ConvertSection(XElement section)
    {
        ArgumentNullException.ThrowIfNull(section);

        var sb = new StringBuilder();
        foreach (XElement child in section.Elements())
        {
            if (child.Name == Fb2Ns + "section")
            {
                continue; // nested section — its own chapter, handled by the caller.
            }

            AppendBlock(child, sb);
        }

        return sb.ToString();
    }

    /// <summary>Emits one direct child element as an HTML block (or, for unknown elements, recurses
    /// into its children).</summary>
    private static void AppendBlock(XElement element, StringBuilder sb)
    {
        switch (element.Name.LocalName)
        {
            case "title":
                AppendHeading(element, "h2", sb);
                break;
            case "subtitle":
                AppendHeading(element, "h3", sb);
                break;
            case "p":
                sb.Append("<p>");
                AppendInline(element, sb);
                sb.Append("</p>");
                break;
            case "epigraph":
            case "cite":
                sb.Append("<blockquote>");
                AppendBlockContainerChildren(element, sb);
                sb.Append("</blockquote>");
                break;
            case "empty-line":
                sb.Append("<br/>");
                break;
            case "emphasis":
            case "strong":
            case "strikethrough":
            case "sub":
            case "sup":
            case "a":
                // Inline element appearing at block level — wrap in a paragraph so it still renders.
                sb.Append("<p>");
                AppendInlineElement(element, sb);
                sb.Append("</p>");
                break;
            default:
                // Unknown block: recurse into children, emitting their blocks/inlines.
                AppendBlockContainerChildren(element, sb);
                break;
        }
    }

    /// <summary>Emits a heading whose text is the concatenation of the element's <c>&lt;p&gt;</c>
    /// lines joined by <c>&lt;br/&gt;</c>; if there are no <c>&lt;p&gt;</c> children, the element's
    /// inline content is used directly.</summary>
    private static void AppendHeading(XElement element, string tag, StringBuilder sb)
    {
        var paragraphs = element.Elements(Fb2Ns + "p").ToList();
        sb.Append('<').Append(tag).Append('>');
        if (paragraphs.Count > 0)
        {
            for (int i = 0; i < paragraphs.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append("<br/>");
                }

                AppendInline(paragraphs[i], sb);
            }
        }
        else
        {
            AppendInline(element, sb);
        }

        sb.Append("</").Append(tag).Append('>');
    }

    /// <summary>Emits the child elements of a block container (epigraph/cite/unknown) as blocks,
    /// recursing so paragraphs and inline runs are preserved.</summary>
    private static void AppendBlockContainerChildren(XElement element, StringBuilder sb)
    {
        foreach (XNode node in element.Nodes())
        {
            switch (node)
            {
                case XElement child when child.Name != Fb2Ns + "section":
                    AppendBlock(child, sb);
                    break;
                case XText text:
                    sb.Append(Encode(text.Value));
                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>Appends the inline content (mixed text + inline elements) of an element, HTML-encoding
    /// raw text.</summary>
    private static void AppendInline(XElement element, StringBuilder sb)
    {
        foreach (XNode node in element.Nodes())
        {
            switch (node)
            {
                case XText text:
                    sb.Append(Encode(text.Value));
                    break;
                case XElement child:
                    AppendInlineElement(child, sb);
                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>Maps a single inline FB2 element to its HTML equivalent (recursing for nested inline
    /// runs); unknown inline elements emit just their text content.</summary>
    private static void AppendInlineElement(XElement element, StringBuilder sb)
    {
        string? tag = element.Name.LocalName switch
        {
            "emphasis" => "em",
            "strong" => "strong",
            "strikethrough" => "s",
            "sub" => "sub",
            "sup" => "sup",
            _ => null,
        };

        if (tag is not null)
        {
            sb.Append('<').Append(tag).Append('>');
            AppendInline(element, sb);
            sb.Append("</").Append(tag).Append('>');
            return;
        }

        // <a> → plain text (href dropped); any other inline/unknown element → its text content.
        AppendInline(element, sb);
    }

    /// <summary>HTML-encodes the three structural characters (<c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>)
    /// in raw FB2 text. Quotes are left as-is — text never lands in an attribute here.</summary>
    private static string Encode(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (text.IndexOfAny(['&', '<', '>']) < 0)
        {
            return text;
        }

        var sb = new StringBuilder(text.Length + 8);
        foreach (char ch in text)
        {
            switch (ch)
            {
                case '&':
                    sb.Append("&amp;");
                    break;
                case '<':
                    sb.Append("&lt;");
                    break;
                case '>':
                    sb.Append("&gt;");
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }

        return sb.ToString();
    }
}
