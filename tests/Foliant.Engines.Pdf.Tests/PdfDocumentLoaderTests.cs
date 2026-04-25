using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Foliant.Engines.Pdf.Tests;

public sealed class PdfDocumentLoaderTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly PdfDocumentLoader _sut = new(NullLogger<PdfDocumentLoader>.Instance);

    public PdfDocumentLoaderTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-pdf-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void Kind_IsPdf() =>
        _sut.Kind.Should().Be(Domain.DocumentKind.Pdf);

    [Fact]
    public void CanLoad_PdfExtension_ReturnsTrue_EvenIfHeaderAbsent()
    {
        var path = Path.Combine(_tmpDir, "doc.pdf");
        File.WriteAllText(path, "not a real pdf");

        _sut.CanLoad(path).Should().BeTrue();
    }

    [Fact]
    public void CanLoad_PdfMagic_ReturnsTrue_EvenWithWrongExtension()
    {
        var path = Path.Combine(_tmpDir, "report.bin");
        File.WriteAllBytes(path, "%PDF-1.7\n..."u8.ToArray());

        _sut.CanLoad(path).Should().BeTrue();
    }

    [Fact]
    public void CanLoad_NoMagic_NoExtension_ReturnsFalse()
    {
        var path = Path.Combine(_tmpDir, "text.txt");
        File.WriteAllText(path, "Hello world");

        _sut.CanLoad(path).Should().BeFalse();
    }

    [Fact]
    public void CanLoad_FileMissing_ReturnsFalse() =>
        _sut.CanLoad(Path.Combine(_tmpDir, "no-such-file.pdf")).Should().BeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CanLoad_BadPath_ReturnsFalse(string? path) =>
        _sut.CanLoad(path!).Should().BeFalse();

    [Fact]
    public async Task LoadAsync_InvalidPdf_ThrowsInvalidOperationException()
    {
        var path = Path.Combine(_tmpDir, "doc.pdf");
        File.WriteAllText(path, "not a real pdf");

        var act = async () => await _sut.LoadAsync(path, default);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*PDFium*");
    }
}
