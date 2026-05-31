using FluentAssertions;
using Foliant.Domain;
using Xunit;

namespace Foliant.Engines.Mobi.Tests;

public sealed class MobiDocumentTests
{
    [Fact]
    public void Parse_SingleTextRecord_ProducesOnePageWithStrippedText()
    {
        byte[] mobi = MobiTestFactory.Build("<html><body><p>Hello MOBI world</p></body></html>", title: "My Book");

        var doc = MobiDocument.Parse(mobi);

        doc.Kind.Should().Be(DocumentKind.Mobi);
        doc.PageCount.Should().Be(1);
        doc.Metadata.Title.Should().Be("My Book");
    }

    [Fact]
    public async Task GetTextLayer_ReturnsStrippedHtmlText()
    {
        byte[] mobi = MobiTestFactory.Build("<html><body><h1>Title</h1><p>Body&nbsp;text</p></body></html>");

        var doc = MobiDocument.Parse(mobi);
        var layer = await doc.GetTextLayerAsync(0, CancellationToken.None);

        layer.Should().NotBeNull();
        layer!.Runs.Should().ContainSingle();
        layer.Runs[0].Text.Should().Contain("Title").And.Contain("Body").And.Contain("text");
        layer.Runs[0].Text.Should().NotContain("<");
    }

    [Fact]
    public void Parse_MultipleTextRecords_ProduceMultiplePages()
    {
        byte[] mobi = MobiTestFactory.Build(
            ["<p>Chapter one</p>", "<p>Chapter two</p>", "<p>Chapter three</p>"]);

        var doc = MobiDocument.Parse(mobi);

        doc.PageCount.Should().Be(3);
    }

    [Fact]
    public async Task RenderPage_ReturnsBlankWhiteBgra()
    {
        byte[] mobi = MobiTestFactory.Build("<p>x</p>");
        var doc = MobiDocument.Parse(mobi);

        var render = await doc.RenderPageAsync(0, new RenderOptions(Zoom: 1.0), CancellationToken.None);

        render.WidthPx.Should().Be(MobiDocument.DefaultPagePxWidth);
        render.Bgra32.Length.Should().Be(render.Stride * render.HeightPx);
        render.Bgra32.Span[0].Should().Be(0xFF); // white
    }

    [Fact]
    public void Parse_TooSmall_Throws()
    {
        var act = () => MobiDocument.Parse(new byte[10]);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void GetPageSize_OutOfRange_Throws()
    {
        var doc = MobiDocument.Parse(MobiTestFactory.Build("<p>x</p>"));

        var act = () => doc.GetPageSize(5);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
