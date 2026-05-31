using FluentAssertions;
using Foliant.Domain;
using Foliant.Engines.Fb2;
using Xunit;

namespace Foliant.Engines.Fb2.Tests;

public sealed class Fb2DocumentTests : IDisposable
{
    private readonly string _tmpDir;

    public Fb2DocumentTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-fb2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ───── CollapseWhitespace (pure, no I/O) ─────

    [Theory]
    [InlineData("hello world", "hello world")]
    [InlineData("  hello   world  ", "hello world")]
    [InlineData("line\n\n\nbreaks", "line breaks")]
    [InlineData("tabs\t\there", "tabs here")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void CollapseWhitespace_NormalisesAllRunsToSingleSpace(string input, string expected)
    {
        Fb2Document.CollapseWhitespaceForTest(input).Should().Be(expected);
    }

    // ───── Open / IDocument surface ─────

    [Fact]
    public void Open_MissingFile_Throws()
    {
        Action act = () => Fb2Document.Open(Path.Combine(_tmpDir, "nope.fb2"));
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Open_BlankPath_Throws()
    {
        Action act = () => Fb2Document.Open("  ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Open_NotXml_ThrowsInvalidData()
    {
        string path = Path.Combine(_tmpDir, "not-xml.fb2");
        File.WriteAllText(path, "This is plain text, not XML.");
        Action act = () => Fb2Document.Open(path);
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Open_XmlButWrongRoot_Throws()
    {
        string path = Path.Combine(_tmpDir, "wrong-root.fb2");
        File.WriteAllText(path, "<?xml version=\"1.0\"?><root/>");
        Action act = () => Fb2Document.Open(path);
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task Open_MinimalFb2_LoadsMetadataAndPages()
    {
        string path = Fb2TestFactory.Create(_tmpDir, "Война и мир", "Лев", "Толстой",
            "Все счастливые семьи похожи друг на друга.",
            "Каждая несчастливая семья несчастлива по-своему.");
        await using var doc = Fb2Document.Open(path);

        doc.Kind.Should().Be(DocumentKind.Fb2);
        doc.PageCount.Should().Be(2);
        doc.Metadata.Title.Should().Be("Война и мир");
        doc.Metadata.Author.Should().Be("Лев Толстой");
    }

    [Fact]
    public async Task GetTextLayerAsync_ReturnsSectionText()
    {
        string path = Fb2TestFactory.Create(_tmpDir, "T", "F", "L",
            "The quick brown fox.");
        await using var doc = Fb2Document.Open(path);

        var layer = await doc.GetTextLayerAsync(0, CancellationToken.None);
        layer.Should().NotBeNull();
        layer!.Runs.Should().ContainSingle();
        layer.Runs[0].Text.Should().Contain("Section 1");      // title
        layer.Runs[0].Text.Should().Contain("quick brown fox"); // paragraph
    }

    [Fact]
    public async Task BodyOnly_NoSections_TreatedAsOnePage()
    {
        string path = Fb2TestFactory.CreateBodyOnly(_tmpDir, "Short", "Just one paragraph of body text.");
        await using var doc = Fb2Document.Open(path);

        doc.PageCount.Should().Be(1);
        var layer = await doc.GetTextLayerAsync(0, CancellationToken.None);
        layer!.Runs[0].Text.Should().Contain("Just one paragraph");
    }

    [Fact]
    public async Task GetTextLayerAsync_OutOfRange_Throws()
    {
        string path = Fb2TestFactory.Create(_tmpDir, "T", "F", "L", "x");
        await using var doc = Fb2Document.Open(path);

        Func<Task> act = async () => await doc.GetTextLayerAsync(5, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task RenderPageAsync_ReturnsBlankWhiteBitmap()
    {
        string path = Fb2TestFactory.Create(_tmpDir, "T", "F", "L", "x");
        await using var doc = Fb2Document.Open(path);

        using var render = await doc.RenderPageAsync(0, new RenderOptions(Zoom: 1.0), CancellationToken.None);
        render.WidthPx.Should().Be(Fb2Document.DefaultPagePxWidth);
        render.HeightPx.Should().Be(Fb2Document.DefaultPagePxHeight);
        var span = render.Bgra32.Span;
        span[0].Should().Be(0xFF);
        span[1].Should().Be(0xFF);
        span[2].Should().Be(0xFF);
        span[3].Should().Be(0xFF);
    }

    [Fact]
    public async Task RenderPageAsync_HonorsMaxWidthOpts()
    {
        string path = Fb2TestFactory.Create(_tmpDir, "T", "F", "L", "x");
        await using var doc = Fb2Document.Open(path);

        using var render = await doc.RenderPageAsync(0, new RenderOptions(Zoom: 1.0, MaxWidthPx: 400), CancellationToken.None);
        render.WidthPx.Should().Be(400);
    }

    [Fact]
    public async Task GetEditor_GetForms_GetSignatures_AllNull()
    {
        string path = Fb2TestFactory.Create(_tmpDir, "T", "F", "L", "x");
        await using var doc = Fb2Document.Open(path);

        doc.GetEditor().Should().BeNull();
        doc.GetForms().Should().BeNull();
        doc.GetSignatures().Should().BeNull();
    }
}
