using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Round-trip тесты для <see cref="PdfPigOcgEditService"/> (rename / delete OCG-слоёв). На
/// каждый кейс собирается хэндкрафтнутый PDF с <c>/OCProperties</c> (см.
/// <see cref="OcgPdfFactory"/>) — pure managed (PdfPig + cos-write), без native PDFium, поэтому
/// без Slow trait (зеркало <see cref="PdfPageLabelServiceTests"/>).
///
/// <para>Результат верифицируется через публичный <see cref="PdfiumOcgService.ReadLayersAsync"/>
/// (он делегирует чтение в тот же internal <c>PdfOcgCosReader.Read</c>, который недоступен
/// тестам напрямую — репозиторий не настраивает <c>InternalsVisibleTo</c> для
/// <c>Foliant.Engines.Pdf</c>), плюс <c>PdfPig</c> для проверки page-count и валидности
/// документа.</para>
/// </summary>
public sealed class PdfOcgEditServiceTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly PdfPigOcgEditService _service = new(NullLogger<PdfPigOcgEditService>.Instance);
    private readonly PdfiumOcgService _reader = new();

    public PdfOcgEditServiceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-ocgedit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tmpDir, recursive: true);
        }
        catch (IOException)
        {
            /* best-effort */
        }
        catch (UnauthorizedAccessException)
        {
            /* best-effort */
        }
    }

    [Fact]
    public async Task Fixture_IsReadableWithExpectedLayers()
    {
        // Verify the fixture itself before exercising edits.
        string src = ThreeLayerFixture();

        var layers = await _reader.ReadLayersAsync(src, default);

        layers.Should().HaveCount(3);
        layers[0].Should().Be(new Domain.PdfLayer(0, "Background", true));
        layers[1].Should().Be(new Domain.PdfLayer(1, "Annotations", false));
        layers[2].Should().Be(new Domain.PdfLayer(2, "Watermark", true));
    }

    [Fact]
    public async Task Rename_LayerOne_UpdatesOnlyThatName()
    {
        string src = ThreeLayerFixture();
        string dst = TargetPath();

        await _service.RenameAsync(src, dst, layerIndex: 1, newName: "Markup", default);

        var after = await _reader.ReadLayersAsync(dst, default);
        after.Should().HaveCount(3);
        after[0].Name.Should().Be("Background");
        after[1].Name.Should().Be("Markup");
        after[2].Name.Should().Be("Watermark");
        // Visibility must be preserved across a rename.
        after[1].IsVisible.Should().BeFalse();
        after[0].IsVisible.Should().BeTrue();
        after[2].IsVisible.Should().BeTrue();
    }

    [Fact]
    public async Task Rename_UnicodeName_RoundTripsLossless()
    {
        string src = ThreeLayerFixture();
        string dst = TargetPath();

        await _service.RenameAsync(src, dst, layerIndex: 0, newName: "Слой-фон", default);

        var after = await _reader.ReadLayersAsync(dst, default);
        after[0].Name.Should().Be("Слой-фон");
        after[1].Name.Should().Be("Annotations");
    }

    [Fact]
    public async Task Rename_PreservesPageCount()
    {
        string src = ThreeLayerFixture();
        string dst = TargetPath();

        await _service.RenameAsync(src, dst, layerIndex: 2, newName: "WM", default);

        PageCount(dst).Should().Be(PageCount(src));
    }

    [Fact]
    public async Task Rename_OutputReopensInPdfPig()
    {
        string src = ThreeLayerFixture();
        string dst = TargetPath();

        await _service.RenameAsync(src, dst, layerIndex: 1, newName: "Markup", default);

        // Reopening must not throw — the incremental update is structurally valid.
        using var doc = PdfPigDocument.Open(dst);
        doc.NumberOfPages.Should().Be(1);
    }

    [Fact]
    public async Task Rename_OutOfRangeIndex_Throws()
    {
        string src = ThreeLayerFixture();

        var act = () => _service.RenameAsync(src, TargetPath(), layerIndex: 5, newName: "X", default);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Rename_NegativeIndex_Throws()
    {
        string src = ThreeLayerFixture();

        var act = () => _service.RenameAsync(src, TargetPath(), layerIndex: -1, newName: "X", default);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rename_BlankNewName_Throws(string blank)
    {
        string src = ThreeLayerFixture();

        var act = () => _service.RenameAsync(src, TargetPath(), layerIndex: 0, newName: blank, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Rename_DoesNotMutateSource()
    {
        string src = ThreeLayerFixture();
        string before = Sha256(src);

        await _service.RenameAsync(src, TargetPath(), layerIndex: 1, newName: "Markup", default);

        Sha256(src).Should().Be(before, "writer must not touch the source file");
    }

    [Fact]
    public async Task Rename_SourceEqualsTarget_IsSafe()
    {
        string path = ThreeLayerFixture();

        await _service.RenameAsync(path, path, layerIndex: 0, newName: "Renamed", default);

        var after = await _reader.ReadLayersAsync(path, default);
        after[0].Name.Should().Be("Renamed");
        after.Should().HaveCount(3);
    }

    [Fact]
    public async Task Remove_LayerOne_DropsToTwoLayersKeepingNames()
    {
        string src = ThreeLayerFixture();
        string dst = TargetPath();

        await _service.RemoveAsync(src, dst, layerIndex: 1, default);

        var after = await _reader.ReadLayersAsync(dst, default);
        after.Should().HaveCount(2);
        // Surviving layers keep their names; indices are re-densified by the reader.
        after.Select(l => l.Name).Should().Equal("Background", "Watermark");
    }

    [Fact]
    public async Task Remove_OutputReopensInPdfPig()
    {
        string src = ThreeLayerFixture();
        string dst = TargetPath();

        await _service.RemoveAsync(src, dst, layerIndex: 1, default);

        using var doc = PdfPigDocument.Open(dst);
        doc.NumberOfPages.Should().Be(1);
    }

    [Fact]
    public async Task Remove_LayerInOffArray_RemovesFromBothOcgsAndD()
    {
        // Layer 1 ("Annotations") is the default-OFF layer; removing it must drop it from
        // /OCGs and from /D /OFF so the survivors read back cleanly with original visibility.
        string src = ThreeLayerFixture();
        string dst = TargetPath();

        await _service.RemoveAsync(src, dst, layerIndex: 1, default);

        var after = await _reader.ReadLayersAsync(dst, default);
        after.Should().HaveCount(2);
        after[0].Should().Be(new Domain.PdfLayer(0, "Background", true));
        after[1].Should().Be(new Domain.PdfLayer(1, "Watermark", true));
    }

    [Fact]
    public async Task Remove_DefaultOffSurvivor_KeepsHiddenState()
    {
        // Remove a visible layer (index 0); the surviving default-OFF layer must stay hidden.
        string src = ThreeLayerFixture();
        string dst = TargetPath();

        await _service.RemoveAsync(src, dst, layerIndex: 0, default);

        var after = await _reader.ReadLayersAsync(dst, default);
        after.Should().HaveCount(2);
        after.Select(l => l.Name).Should().Equal("Annotations", "Watermark");
        after[0].IsVisible.Should().BeFalse("the removed layer's /OFF entry must not disturb survivors");
        after[1].IsVisible.Should().BeTrue();
    }

    [Fact]
    public async Task Remove_PreservesPageCount()
    {
        string src = ThreeLayerFixture();
        string dst = TargetPath();

        await _service.RemoveAsync(src, dst, layerIndex: 2, default);

        PageCount(dst).Should().Be(PageCount(src));
    }

    [Fact]
    public async Task Remove_LastRemainingLayer_YieldsEmptyLayerList()
    {
        string src = OcgPdfFactory.Write(_tmpDir, new[] { "Only" }, offIndices: Array.Empty<int>());
        string dst = TargetPath();

        await _service.RemoveAsync(src, dst, layerIndex: 0, default);

        var after = await _reader.ReadLayersAsync(dst, default);
        after.Should().BeEmpty();
        PageCount(dst).Should().Be(1);
    }

    [Fact]
    public async Task Remove_OutOfRangeIndex_Throws()
    {
        string src = ThreeLayerFixture();

        var act = () => _service.RemoveAsync(src, TargetPath(), layerIndex: 9, default);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Remove_NegativeIndex_Throws()
    {
        string src = ThreeLayerFixture();

        var act = () => _service.RemoveAsync(src, TargetPath(), layerIndex: -3, default);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Remove_DoesNotMutateSource()
    {
        string src = ThreeLayerFixture();
        string before = Sha256(src);

        await _service.RemoveAsync(src, TargetPath(), layerIndex: 1, default);

        Sha256(src).Should().Be(before, "writer must not touch the source file");
    }

    [Fact]
    public async Task Remove_SourceEqualsTarget_IsSafe()
    {
        string path = ThreeLayerFixture();

        await _service.RemoveAsync(path, path, layerIndex: 1, default);

        var after = await _reader.ReadLayersAsync(path, default);
        after.Should().HaveCount(2);
        after.Select(l => l.Name).Should().Equal("Background", "Watermark");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rename_BlankSource_Throws(string blank)
    {
        var act = () => _service.RenameAsync(blank, TargetPath(), 0, "X", default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rename_BlankTarget_Throws(string blank)
    {
        string src = ThreeLayerFixture();

        var act = () => _service.RenameAsync(src, blank, 0, "X", default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Remove_BlankSource_Throws(string blank)
    {
        var act = () => _service.RemoveAsync(blank, TargetPath(), 0, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Remove_BlankTarget_Throws(string blank)
    {
        string src = ThreeLayerFixture();

        var act = () => _service.RemoveAsync(src, blank, 0, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private string ThreeLayerFixture() => OcgPdfFactory.Write(
        _tmpDir,
        new[] { "Background", "Annotations", "Watermark" },
        offIndices: new[] { 1 });

    private string TargetPath() => Path.Combine(_tmpDir, "out-" + Guid.NewGuid().ToString("N") + ".pdf");

    private static int PageCount(string path)
    {
        using var doc = PdfPigDocument.Open(path);
        return doc.NumberOfPages;
    }

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
