using FluentAssertions;
using Xunit;

namespace Foliant.Domain.Tests;

public sealed class TextLayerTests
{
    [Fact]
    public void Empty_ReturnsLayerWithNoRuns()
    {
        var layer = TextLayer.Empty(7);

        layer.PageIndex.Should().Be(7);
        layer.Runs.Should().BeEmpty();
        layer.ToPlainText().Should().BeEmpty();
    }

    [Fact]
    public void ToPlainText_ConcatenatesRuns_InOrder()
    {
        var layer = new TextLayer(0, [
            new TextRun("Hello, ", 0, 0, 100, 12),
            new TextRun("Foliant", 100, 0, 80, 12),
            new TextRun("!",      180, 0, 5,  12),
        ]);

        layer.ToPlainText().Should().Be("Hello, Foliant!");
    }
}
