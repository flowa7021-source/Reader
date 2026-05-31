using FluentAssertions;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Engines.Mobi.Tests;

public sealed class MobiDocumentLoaderTests : IDisposable
{
    private readonly string _tmpDir = Path.Combine(Path.GetTempPath(), "mobi-loader-" + Guid.NewGuid().ToString("N"));
    private readonly MobiDocumentLoader _sut = new(NullLogger<MobiDocumentLoader>.Instance);

    public MobiDocumentLoaderTests() => Directory.CreateDirectory(_tmpDir);

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void Kind_IsMobi()
    {
        _sut.Kind.Should().Be(DocumentKind.Mobi);
    }

    [Fact]
    public void CanLoad_MobiExtension_True()
    {
        string path = MobiTestFactory.WriteToFile(_tmpDir, "book.mobi", "<p>x</p>");

        _sut.CanLoad(path).Should().BeTrue();
    }

    [Fact]
    public void CanLoad_NoExtension_SniffsBookMobiSignature()
    {
        string path = MobiTestFactory.WriteToFile(_tmpDir, "book.bin", "<p>sniff me</p>");

        _sut.CanLoad(path).Should().BeTrue();
    }

    [Fact]
    public void CanLoad_NonMobi_False()
    {
        string path = Path.Combine(_tmpDir, "not-mobi.bin");
        File.WriteAllText(path, "just some text, definitely not a PalmDB container at all");

        _sut.CanLoad(path).Should().BeFalse();
    }

    [Fact]
    public void CanLoad_MissingFile_False()
    {
        _sut.CanLoad(Path.Combine(_tmpDir, "ghost.mobi")).Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_ValidMobi_ReturnsDocumentWithPages()
    {
        string path = MobiTestFactory.WriteToFile(_tmpDir, "book.mobi", "<p>Hello from disk</p>", title: "Disk Book");

        IDocument doc = await _sut.LoadAsync(path, CancellationToken.None);

        doc.Kind.Should().Be(DocumentKind.Mobi);
        doc.PageCount.Should().BeGreaterThan(0);
        doc.Metadata.Title.Should().Be("Disk Book");
    }

    [Fact]
    public async Task LoadAsync_MissingFile_Throws()
    {
        var act = async () => await _sut.LoadAsync(Path.Combine(_tmpDir, "ghost.mobi"), CancellationToken.None);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }
}
