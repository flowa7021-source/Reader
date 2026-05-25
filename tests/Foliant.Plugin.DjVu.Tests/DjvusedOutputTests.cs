using FluentAssertions;
using Foliant.Domain;
using Xunit;

namespace Foliant.Plugin.DjVu.Tests;

public sealed class DjvusedOutputTests
{
    [Fact]
    public void ParsePageCount_ReadsInteger() =>
        DjvusedOutput.ParsePageCount("42\n").Should().Be(42);

    [Fact]
    public void ParsePageCount_Garbage_Throws()
    {
        var act = () => DjvusedOutput.ParsePageCount("not-a-number");

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void ParsePageSize_ReadsWidthAndHeight()
    {
        PageSize size = DjvusedOutput.ParsePageSize("width=2480 height=3508 dpi=300\n");

        size.WidthPt.Should().Be(2480);
        size.HeightPt.Should().Be(3508);
    }

    [Fact]
    public void ParseTextLayer_ParsesWordRuns()
    {
        const string SExpr = """
            (page 0 0 2480 3508
             (line 100 200 400 260
              (word 100 200 250 260 "Hello")
              (word 260 200 400 260 "World")))
            """;

        TextLayer layer = DjvusedOutput.ParseTextLayer(0, SExpr);

        layer.Runs.Should().HaveCount(2);
        layer.Runs[0].Text.Should().Be("Hello");
        layer.Runs[0].X.Should().Be(100);
        layer.Runs[0].W.Should().Be(150); // 250 - 100
        layer.Runs[1].Text.Should().Be("World");
    }

    [Fact]
    public void ParseTextLayer_NoWords_ReturnsEmpty()
    {
        TextLayer layer = DjvusedOutput.ParseTextLayer(3, "(page 0 0 100 100)");

        layer.Runs.Should().BeEmpty();
        layer.PageIndex.Should().Be(3);
    }
}
