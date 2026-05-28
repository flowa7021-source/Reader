using FluentAssertions;
using Foliant.Domain;
using Foliant.Engines.Image;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Engines.Image.Tests;

public sealed class ImageDocumentLoaderTests
{
    private readonly ImageDocumentLoader _sut = new(NullLogger<ImageDocumentLoader>.Instance);

    [Fact]
    public void Kind_IsImage()
    {
        _sut.Kind.Should().Be(DocumentKind.Image);
    }

    [Theory]
    [InlineData("Assets/tiny.png")]
    [InlineData("Assets/tiny.jpg")]
    public void CanLoad_KnownExtension_ReturnsTrue(string path)
    {
        _sut.CanLoad(path).Should().BeTrue();
    }

    [Fact]
    public void CanLoad_PngMagicDespiteWrongExtension_ReturnsTrue()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "foliant-png-" + Guid.NewGuid().ToString("N") + ".bin");
        File.Copy("Assets/tiny.png", tmp);
        try
        {
            _sut.CanLoad(tmp).Should().BeTrue();
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void CanLoad_NonImageFile_ReturnsFalse()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "foliant-notimg-" + Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllText(tmp, "this is plain text, not an image");
        try
        {
            _sut.CanLoad(tmp).Should().BeFalse();
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void CanLoad_MissingPath_ReturnsFalse()
    {
        _sut.CanLoad(Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".png"))
            .Should().BeFalse();
    }

    [Fact]
    public void CanLoad_NullOrEmpty_ReturnsFalse()
    {
        _sut.CanLoad("").Should().BeFalse();
        _sut.CanLoad("   ").Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_TinyPng_ReturnsSinglePageDocument()
    {
        await using IDocument doc = await _sut.LoadAsync("Assets/tiny.png", CancellationToken.None);

        doc.Kind.Should().Be(DocumentKind.Image);
        doc.PageCount.Should().Be(1);
        doc.Metadata.Should().BeSameAs(DocumentMetadata.Empty);
        doc.GetEditor().Should().BeNull();
        doc.GetForms().Should().BeNull();
        doc.GetSignatures().Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_TinyPng_RendersBgra32WithCorrectStride()
    {
        await using IDocument doc = await _sut.LoadAsync("Assets/tiny.png", CancellationToken.None);

        using IPageRender render = await doc.RenderPageAsync(0, RenderOptions.Default, CancellationToken.None);

        render.WidthPx.Should().Be(4);
        render.HeightPx.Should().Be(3);
        render.Stride.Should().Be(16);
        render.Bgra32.Length.Should().Be(48);
        render.PageSize.WidthPt.Should().Be(4);
        render.PageSize.HeightPt.Should().Be(3);

        // First pixel of row 0 = opaque red (RGBA 255,0,0,255) → BGRA bytes 0,0,255,255.
        ReadOnlySpan<byte> bgra = render.Bgra32.Span;
        bgra[0].Should().Be(0);   // B
        bgra[1].Should().Be(0);   // G
        bgra[2].Should().Be(255); // R
        bgra[3].Should().Be(255); // A
    }

    [Fact]
    public async Task LoadAsync_PageSizeMatchesPixels()
    {
        await using IDocument doc = await _sut.LoadAsync("Assets/tiny.png", CancellationToken.None);

        PageSize size = doc.GetPageSize(0);

        size.WidthPt.Should().Be(4);
        size.HeightPt.Should().Be(3);
    }

    [Fact]
    public async Task LoadAsync_MissingFile_Throws()
    {
        string missing = Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".png");

        Func<Task> act = async () => await _sut.LoadAsync(missing, CancellationToken.None);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task LoadAsync_NotAnImage_Throws()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "foliant-bad-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllText(tmp, "not an image, just text with a .png name");
        try
        {
            Func<Task> act = async () => await _sut.LoadAsync(tmp, CancellationToken.None);

            await act.Should().ThrowAsync<Exception>();
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task RenderPageAsync_InvalidPageIndex_Throws()
    {
        await using IDocument doc = await _sut.LoadAsync("Assets/tiny.png", CancellationToken.None);

        Func<Task> act = async () => await doc.RenderPageAsync(1, RenderOptions.Default, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task GetTextLayerAsync_ReturnsNull()
    {
        await using IDocument doc = await _sut.LoadAsync("Assets/tiny.png", CancellationToken.None);

        TextLayer? layer = await doc.GetTextLayerAsync(0, CancellationToken.None);

        layer.Should().BeNull();
    }
}
