using System.Security.Cryptography;
using FluentAssertions;
using Foliant.Engines.Pdf;
using Xunit;

namespace Foliant.Engines.Pdf.Tests;

/// <summary>
/// Slow round-trip для OCG (Optional Content Groups): на каждый кейс собирается
/// хэндкрафтнутый PDF с <c>/OCProperties</c> (см. <see cref="OcgPdfFactory"/>),
/// проходит через <see cref="PdfiumOcgService"/>, и результат верифицируется тем же
/// сервисом для чтения.
/// </summary>
[Trait("Category", "Slow")]
public sealed class PdfiumOcgServiceTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly PdfiumOcgService _svc;

    public PdfiumOcgServiceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-ocg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
        _svc = new PdfiumOcgService();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task ReadLayersAsync_PdfWithThreeLayers_ReturnsAllNamesAndDefaults()
    {
        // Layer 0 default ON, layer 1 default OFF, layer 2 default ON — стандартный набор.
        string src = OcgPdfFactory.Write(
            _tmpDir,
            new[] { "Background", "Annotations", "Watermark" },
            offIndices: new[] { 1 });

        var layers = await _svc.ReadLayersAsync(src, CancellationToken.None);

        layers.Should().HaveCount(3);
        layers[0].Should().Be(new Domain.PdfLayer(0, "Background", true));
        layers[1].Should().Be(new Domain.PdfLayer(1, "Annotations", false));
        layers[2].Should().Be(new Domain.PdfLayer(2, "Watermark", true));
    }

    [Fact]
    public async Task ReadLayersAsync_PdfWithoutOCG_ReturnsEmpty()
    {
        // Reuse MinimalPdfFactory (без /OCProperties), результат — пустой список.
        string src = Path.Combine(_tmpDir, "plain.pdf");
        await File.WriteAllBytesAsync(src, MinimalPdfFactory.Create(), CancellationToken.None);

        var layers = await _svc.ReadLayersAsync(src, CancellationToken.None);

        layers.Should().BeEmpty();
    }

    [Fact]
    public async Task SetLayerVisibilityAsync_HidesLayerZero_OnlyThatLayerFlips()
    {
        // Все три слоя default-видимы; гасим только слой #0.
        string src = OcgPdfFactory.Write(
            _tmpDir,
            new[] { "Background", "Annotations", "Watermark" },
            offIndices: Array.Empty<int>());
        string dst = Path.Combine(_tmpDir, "after-toggle.pdf");

        await _svc.SetLayerVisibilityAsync(
            src, dst,
            new Dictionary<int, bool> { [0] = false },
            CancellationToken.None);

        var after = await _svc.ReadLayersAsync(dst, CancellationToken.None);
        after.Should().HaveCount(3);
        after[0].IsVisible.Should().BeFalse();
        after[1].IsVisible.Should().BeTrue();
        after[2].IsVisible.Should().BeTrue();
        // Имена не должны поменяться.
        after[0].Name.Should().Be("Background");
        after[2].Name.Should().Be("Watermark");
    }

    [Fact]
    public async Task SetLayerVisibilityAsync_EmptyDictionary_PreservesAllLayers()
    {
        string src = OcgPdfFactory.Write(
            _tmpDir,
            new[] { "Layer A", "Layer B" },
            offIndices: new[] { 1 });
        string dst = Path.Combine(_tmpDir, "passthrough.pdf");

        await _svc.SetLayerVisibilityAsync(
            src, dst,
            new Dictionary<int, bool>(),
            CancellationToken.None);

        File.Exists(dst).Should().BeTrue();
        var after = await _svc.ReadLayersAsync(dst, CancellationToken.None);
        after.Should().HaveCount(2);
        // Default-visibility сохранён (слой #1 как был OFF, так и остался).
        after[0].IsVisible.Should().BeTrue();
        after[1].IsVisible.Should().BeFalse();
        after[0].Name.Should().Be("Layer A");
        after[1].Name.Should().Be("Layer B");
    }

    [Fact]
    public async Task SetLayerVisibilityAsync_OutOfRangeIndex_IsIgnoredSilently()
    {
        string src = OcgPdfFactory.Write(
            _tmpDir,
            new[] { "Only" },
            offIndices: Array.Empty<int>());
        string dst = Path.Combine(_tmpDir, "ignored.pdf");

        // Index 5 не существует — должны проигнорировать, не упасть.
        await _svc.SetLayerVisibilityAsync(
            src, dst,
            new Dictionary<int, bool> { [5] = false, [0] = false },
            CancellationToken.None);

        var after = await _svc.ReadLayersAsync(dst, CancellationToken.None);
        after.Should().HaveCount(1);
        after[0].IsVisible.Should().BeFalse(); // index 0 применился, index 5 проигнорирован
    }

    [Fact]
    public async Task SetLayerVisibilityAsync_DoesNotMutateOriginal()
    {
        string src = OcgPdfFactory.Write(
            _tmpDir,
            new[] { "L1", "L2" },
            offIndices: Array.Empty<int>());
        string dst = Path.Combine(_tmpDir, "preserves-original.pdf");
        string hashBefore = Sha256OfFile(src);

        await _svc.SetLayerVisibilityAsync(
            src, dst,
            new Dictionary<int, bool> { [0] = false, [1] = false },
            CancellationToken.None);

        string hashAfter = Sha256OfFile(src);
        hashAfter.Should().Be(hashBefore, "оригинал не должен мутироваться (паттерн watermark/redact)");
    }

    [Fact]
    public async Task SetLayerVisibilityAsync_ReEnablesPreviouslyHiddenLayer()
    {
        // слой #0 был OFF; включаем обратно через {0: true}.
        string src = OcgPdfFactory.Write(
            _tmpDir,
            new[] { "Hidden", "Visible" },
            offIndices: new[] { 0 });
        string dst = Path.Combine(_tmpDir, "re-enabled.pdf");

        await _svc.SetLayerVisibilityAsync(
            src, dst,
            new Dictionary<int, bool> { [0] = true },
            CancellationToken.None);

        var after = await _svc.ReadLayersAsync(dst, CancellationToken.None);
        after[0].IsVisible.Should().BeTrue();
        after[1].IsVisible.Should().BeTrue();
    }

    [Fact]
    public async Task ReadLayersAsync_BlankPath_Throws()
    {
        Func<Task> act = async () => await _svc.ReadLayersAsync("   ", CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SetLayerVisibilityAsync_NullDictionary_Throws()
    {
        string src = OcgPdfFactory.Write(
            _tmpDir, new[] { "X" }, offIndices: Array.Empty<int>());
        Func<Task> act = async () => await _svc.SetLayerVisibilityAsync(
            src, Path.Combine(_tmpDir, "out.pdf"), null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private static string Sha256OfFile(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        byte[] hash = sha.ComputeHash(fs);
        return Convert.ToHexString(hash);
    }
}
