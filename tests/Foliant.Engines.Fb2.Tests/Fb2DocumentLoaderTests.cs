using FluentAssertions;
using Foliant.Domain;
using Foliant.Engines.Fb2;
using Foliant.Rendering.Html;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Engines.Fb2.Tests;

public sealed class Fb2DocumentLoaderTests : IDisposable
{
    private static readonly IHtmlRenderer Renderer = new HtmlRenderer(new FontStore(), NullLogger<HtmlRenderer>.Instance);

    private readonly string _tmpDir;
    private readonly Fb2DocumentLoader _loader;

    public Fb2DocumentLoaderTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-fb2-loader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
        _loader = new Fb2DocumentLoader(NullLogger<Fb2DocumentLoader>.Instance, Renderer);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void Kind_IsFb2()
    {
        _loader.Kind.Should().Be(DocumentKind.Fb2);
    }

    [Fact]
    public void CanLoad_BlankOrMissing_False()
    {
        _loader.CanLoad("").Should().BeFalse();
        _loader.CanLoad("   ").Should().BeFalse();
        _loader.CanLoad(Path.Combine(_tmpDir, "missing.fb2")).Should().BeFalse();
    }

    [Fact]
    public void CanLoad_Fb2ByExtension_True()
    {
        string path = Fb2TestFactory.Create(_tmpDir, "T", "F", "L", "x");
        _loader.CanLoad(path).Should().BeTrue();
    }

    [Fact]
    public void CanLoad_ExtensionlessButFictionBookSniff_True()
    {
        string original = Fb2TestFactory.Create(_tmpDir, "T", "F", "L", "x");
        string renamed = Path.Combine(_tmpDir, "extensionless-fictionbook");
        File.Move(original, renamed);

        _loader.CanLoad(renamed).Should().BeTrue();
    }

    [Fact]
    public void CanLoad_ExtensionlessNonFb2_False()
    {
        string path = Path.Combine(_tmpDir, "plain-text-no-ext");
        // Note: intentionally avoid the literal word that the sniff scans for.
        File.WriteAllText(path, "Just some plain text without the magic marker word.");
        _loader.CanLoad(path).Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_ReturnsFb2Document()
    {
        string path = Fb2TestFactory.Create(_tmpDir, "MyBook", "MyFirst", "MyLast",
            "Section A text.", "Section B text.");

        await using var doc = await _loader.LoadAsync(path, CancellationToken.None);

        doc.Kind.Should().Be(DocumentKind.Fb2);
        doc.PageCount.Should().Be(2);
        doc.Metadata.Title.Should().Be("MyBook");
    }

    [Fact]
    public async Task LoadAsync_MissingFile_Throws()
    {
        Func<Task> act = async () => await _loader.LoadAsync(Path.Combine(_tmpDir, "nope.fb2"), CancellationToken.None);
        await act.Should().ThrowAsync<FileNotFoundException>();
    }
}
