using FluentAssertions;
using Foliant.Domain;
using Foliant.Engines.Epub;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Engines.Epub.Tests;

public sealed class EpubDocumentLoaderTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly EpubDocumentLoader _loader;

    public EpubDocumentLoaderTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-epub-loader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
        _loader = new EpubDocumentLoader(NullLogger<EpubDocumentLoader>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void Kind_IsEpub()
    {
        _loader.Kind.Should().Be(DocumentKind.Epub);
    }

    [Fact]
    public void CanLoad_BlankPath_False()
    {
        _loader.CanLoad("").Should().BeFalse();
        _loader.CanLoad("   ").Should().BeFalse();
    }

    [Fact]
    public void CanLoad_NonExistentPath_False()
    {
        _loader.CanLoad(Path.Combine(_tmpDir, "missing.epub")).Should().BeFalse();
    }

    [Fact]
    public void CanLoad_EpubByExtension_True()
    {
        string path = EpubTestFactory.Create(_tmpDir, "T", "A", "<p>x</p>");
        _loader.CanLoad(path).Should().BeTrue();
    }

    [Fact]
    public void CanLoad_ExtensionlessButValidZipWithEpubMimetype_True()
    {
        string original = EpubTestFactory.Create(_tmpDir, "T", "A", "<p>x</p>");
        string renamed = Path.Combine(_tmpDir, "extensionless-binary");
        File.Move(original, renamed);

        _loader.CanLoad(renamed).Should().BeTrue();
    }

    [Fact]
    public void CanLoad_ExtensionlessNonZip_False()
    {
        // Без .epub-расширения CanLoad полагается на magic-sniff;
        // plain-text без ZIP-header → false.
        string path = Path.Combine(_tmpDir, "not-zip-no-ext");
        File.WriteAllText(path, "Just some plain text, not a ZIP at all.");
        _loader.CanLoad(path).Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_ReturnsEpubDocument()
    {
        string path = EpubTestFactory.Create(_tmpDir, "MyBook", "MyAuthor",
            "<h1>Ch1</h1><p>Hello</p>",
            "<h1>Ch2</h1><p>World</p>");

        await using var doc = await _loader.LoadAsync(path, CancellationToken.None);

        doc.Kind.Should().Be(DocumentKind.Epub);
        doc.PageCount.Should().Be(2);
        doc.Metadata.Title.Should().Be("MyBook");
    }

    [Fact]
    public async Task LoadAsync_MissingFile_Throws()
    {
        Func<Task> act = async () => await _loader.LoadAsync(Path.Combine(_tmpDir, "nope.epub"), CancellationToken.None);
        await act.Should().ThrowAsync<FileNotFoundException>();
    }
}
