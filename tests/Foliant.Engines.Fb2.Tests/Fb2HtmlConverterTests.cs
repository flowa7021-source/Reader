using System.Xml.Linq;
using FluentAssertions;
using Foliant.Engines.Fb2;
using Xunit;

namespace Foliant.Engines.Fb2.Tests;

/// <summary>
/// Unit tests for <see cref="Fb2HtmlConverter"/> — a pure <see cref="XElement"/>→HTML transform.
/// Each fixture wraps the FB2 markup in a namespaced <c>&lt;section&gt;</c> so the converter sees the
/// real gribuser namespace it matches against.
/// </summary>
public sealed class Fb2HtmlConverterTests
{
    private const string Fb2NsDecl = "xmlns=\"http://www.gribuser.ru/xml/fictionbook/2.0\"";

    private static string Convert(string sectionInnerXml)
    {
        XElement section = XElement.Parse($"<section {Fb2NsDecl}>{sectionInnerXml}</section>");
        return Fb2HtmlConverter.ConvertSection(section);
    }

    [Fact]
    public void Title_BecomesH2()
    {
        Convert("<title><p>Chapter One</p></title>")
            .Should().Be("<h2>Chapter One</h2>");
    }

    [Fact]
    public void Title_MultipleParagraphs_JoinedByBr()
    {
        Convert("<title><p>Part I</p><p>The Beginning</p></title>")
            .Should().Be("<h2>Part I<br/>The Beginning</h2>");
    }

    [Fact]
    public void Subtitle_BecomesH3()
    {
        Convert("<subtitle>A quiet morning</subtitle>")
            .Should().Be("<h3>A quiet morning</h3>");
    }

    [Fact]
    public void Paragraph_BecomesP()
    {
        Convert("<p>Hello world.</p>")
            .Should().Be("<p>Hello world.</p>");
    }

    [Fact]
    public void Emphasis_BecomesEm()
    {
        Convert("<p>It was the <emphasis>best</emphasis> of times.</p>")
            .Should().Be("<p>It was the <em>best</em> of times.</p>");
    }

    [Fact]
    public void Strong_BecomesStrong()
    {
        Convert("<p>A <strong>bold</strong> claim.</p>")
            .Should().Be("<p>A <strong>bold</strong> claim.</p>");
    }

    [Fact]
    public void Strikethrough_BecomesS()
    {
        Convert("<p>Old <strikethrough>price</strikethrough>.</p>")
            .Should().Be("<p>Old <s>price</s>.</p>");
    }

    [Fact]
    public void SubAndSup_PassThrough()
    {
        Convert("<p>H<sub>2</sub>O and x<sup>2</sup>.</p>")
            .Should().Be("<p>H<sub>2</sub>O and x<sup>2</sup>.</p>");
    }

    [Fact]
    public void Anchor_BecomesPlainText_HrefDropped()
    {
        Convert("<p>See <a xlink:href=\"#note1\" xmlns:xlink=\"http://www.w3.org/1999/xlink\">note</a>.</p>")
            .Should().Be("<p>See note.</p>");
    }

    [Fact]
    public void EmptyLine_BecomesBr()
    {
        Convert("<p>Above.</p><empty-line/><p>Below.</p>")
            .Should().Be("<p>Above.</p><br/><p>Below.</p>");
    }

    [Fact]
    public void Epigraph_BecomesBlockquote()
    {
        Convert("<epigraph><p>To be or not to be.</p></epigraph>")
            .Should().Be("<blockquote><p>To be or not to be.</p></blockquote>");
    }

    [Fact]
    public void Cite_BecomesBlockquote()
    {
        Convert("<cite><p>Cogito ergo sum.</p></cite>")
            .Should().Be("<blockquote><p>Cogito ergo sum.</p></blockquote>");
    }

    [Fact]
    public void TextContent_IsHtmlEncoded()
    {
        Convert("<p>a &amp; b &lt; c &gt; d</p>")
            .Should().Be("<p>a &amp; b &lt; c &gt; d</p>");
    }

    [Fact]
    public void NestedSection_IsSkipped()
    {
        Convert("<p>Outer.</p><section><p>Inner.</p></section>")
            .Should().Be("<p>Outer.</p>");
    }

    [Fact]
    public void UnknownElement_RecursesIntoChildren()
    {
        Convert("<poem><stanza><p>A line.</p></stanza></poem>")
            .Should().Be("<p>A line.</p>");
    }

    [Fact]
    public void EmptySection_ProducesEmptyFragment()
    {
        Convert(string.Empty).Should().BeEmpty();
    }
}
