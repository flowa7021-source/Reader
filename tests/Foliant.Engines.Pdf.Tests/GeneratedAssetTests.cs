using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using UglyToad.PdfPig;
using Xunit;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Validates the synthetic golden assets produced by <c>tools/gen-test-pdfs</c>.
/// The fast tests use PdfPig (managed, cross-platform) so they stay green under the
/// Linux unit filter; the PDFium round-trip via the production loader is marked Slow.
/// </summary>
public sealed class GeneratedAssetTests
{
    private static string AssetsDir => ResolveAssetsDir();

    [Theory]
    [InlineData("pdf-text-en-10p.pdf")]
    [InlineData("pdf-text-ru-10p.pdf")]
    public void ValidPdf_OpensWithTenPages(string name)
    {
        string path = Path.Combine(AssetsDir, name);
        File.Exists(path).Should().BeTrue($"'{name}' should be committed under tests/assets");

        using var doc = PdfDocument.Open(path);
        doc.NumberOfPages.Should().Be(10);
    }

    [Theory]
    [InlineData("broken-truncated.pdf")]
    [InlineData("broken-empty.pdf")]
    public void BrokenPdf_FailsToOpen(string name)
    {
        string path = Path.Combine(AssetsDir, name);
        File.Exists(path).Should().BeTrue();

        var act = () => PdfDocument.Open(path);

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void BadXrefPdf_FailsUnderStrictParsing()
    {
        string path = Path.Combine(AssetsDir, "broken-bad-xref.pdf");
        File.Exists(path).Should().BeTrue();

        var act = () => PdfDocument.Open(path, ParsingOptions.LenientParsingOff);

        act.Should().Throw<Exception>();
    }

    [Theory]
    [Trait("Category", "Slow")]
    [InlineData("pdf-text-en-10p.pdf")]
    [InlineData("pdf-text-ru-10p.pdf")]
    public async Task ValidPdf_OpensViaPdfiumLoader_WithTenPages(string name)
    {
        var loader = new PdfDocumentLoader(NullLogger<PdfDocumentLoader>.Instance);
        string path = Path.Combine(AssetsDir, name);

        await using var doc = await loader.LoadAsync(path, default);

        doc.PageCount.Should().Be(10);
    }

    // broken-bad-xref.pdf намеренно НЕ здесь: PDFium восстанавливает повреждённую xref-таблицу
    // (rebuild-xref — штатное lenient-поведение ридера, как Acrobat/Foxit), поэтому файл
    // открывается без ошибки. Это корректно: реальные PDF с битым xref должны открываться.
    // Строгий разбор bad-xref проверяется отдельно в BadXrefPdf_FailsUnderStrictParsing
    // (PdfPig, LenientParsingOff). Здесь — только реально неоткрываемые файлы.
    [Theory]
    [Trait("Category", "Slow")]
    [InlineData("broken-truncated.pdf")]
    [InlineData("broken-empty.pdf")]
    public async Task BrokenPdf_FailsViaPdfiumLoader(string name)
    {
        var loader = new PdfDocumentLoader(NullLogger<PdfDocumentLoader>.Instance);
        string path = Path.Combine(AssetsDir, name);

        var act = async () => await loader.LoadAsync(path, default);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*PDFium*");
    }

    // Контр-тест к комментарию выше: тот же bad-xref ассет через ленивый PDFium-loader
    // (как в проде) ОТКРЫВАЕТСЯ — фиксирует, что recovery работает и регрессия его не сломает.
    [Fact]
    [Trait("Category", "Slow")]
    public async Task BadXrefPdf_RecoversViaPdfiumLoader()
    {
        var loader = new PdfDocumentLoader(NullLogger<PdfDocumentLoader>.Instance);
        string path = Path.Combine(AssetsDir, "broken-bad-xref.pdf");

        await using var doc = await loader.LoadAsync(path, default);

        doc.PageCount.Should().BeGreaterThan(0);
    }

    private static string ResolveAssetsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Foliant.sln")))
            {
                return Path.Combine(dir.FullName, "tests", "assets");
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root (Foliant.sln).");
    }
}
