using FluentAssertions;
using Foliant.Domain;
using Xunit;

namespace Foliant.Plugin.DjVu.Tests;

public sealed class DjvuDocumentLoaderTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly DjvuDocumentLoader _sut = new();

    public DjvuDocumentLoaderTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-djvu-tests-" + Guid.NewGuid().ToString("N"));
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
    public void Kind_IsDjvu() => _sut.Kind.Should().Be(DocumentKind.Djvu);

    [Theory]
    [InlineData("book.djvu")]
    [InlineData("scan.DJV")]
    public void CanLoad_DjvuExtension_ReturnsTrue(string name)
    {
        string path = Path.Combine(_tmpDir, name);
        File.WriteAllText(path, "x");

        _sut.CanLoad(path).Should().BeTrue();
    }

    [Fact]
    public void CanLoad_AttMagic_ReturnsTrue_EvenWithWrongExtension()
    {
        string path = Path.Combine(_tmpDir, "blob.bin");
        File.WriteAllBytes(path, "AT&TFORM"u8.ToArray());

        _sut.CanLoad(path).Should().BeTrue();
    }

    [Fact]
    public void CanLoad_NoMagicNoExtension_ReturnsFalse()
    {
        string path = Path.Combine(_tmpDir, "notes.txt");
        File.WriteAllText(path, "plain text");

        _sut.CanLoad(path).Should().BeFalse();
    }

    [Fact]
    public void CanLoad_FileMissing_ReturnsFalse() =>
        _sut.CanLoad(Path.Combine(_tmpDir, "ghost.djvu")).Should().BeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CanLoad_BadPath_ReturnsFalse(string? path) =>
        _sut.CanLoad(path!).Should().BeFalse();

    [Fact]
    public async Task LoadAsync_MissingFile_ThrowsFileNotFound()
    {
        string path = Path.Combine(_tmpDir, "ghost.djvu");

        var act = async () => await _sut.LoadAsync(path, default);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }
}
