using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelOcrTests
{
    private static DocumentTabViewModel CreateVm(
        IOcrPipelineService? ocr,
        IFileFingerprint? fingerprint,
        int pageCount = 3)
    {
        var doc = Substitute.For<IDocument>();
        doc.PageCount.Returns(pageCount);

        return new DocumentTabViewModel(
            doc,
            "/tmp/scan.pdf",
            Substitute.For<ISearchService>(),
            Substitute.For<IAnnotationService>(),
            Substitute.For<IBookmarkService>(),
            NullLogger<DocumentTabViewModel>.Instance,
            ocr: ocr,
            fingerprint: fingerprint);
    }

    [Fact]
    public void CanRunOcr_WhenEngineAndFingerprintProvided_IsTrue()
    {
        var vm = CreateVm(Substitute.For<IOcrPipelineService>(), Substitute.For<IFileFingerprint>());

        vm.CanRunOcr.Should().BeTrue();
    }

    [Fact]
    public void CanRunOcr_WhenEngineMissing_IsFalse()
    {
        var vm = CreateVm(ocr: null, fingerprint: Substitute.For<IFileFingerprint>());

        vm.CanRunOcr.Should().BeFalse();
    }

    [Fact]
    public void CanRunOcr_WhenDocumentEmpty_IsFalse()
    {
        var vm = CreateVm(Substitute.For<IOcrPipelineService>(), Substitute.For<IFileFingerprint>(), pageCount: 0);

        vm.CanRunOcr.Should().BeFalse();
    }

    [Fact]
    public async Task RunOcr_InvokesPipeline_StoresLayersAndStatus()
    {
        var fingerprint = Substitute.For<IFileFingerprint>();
        fingerprint.ComputeAsync("/tmp/scan.pdf", Arg.Any<CancellationToken>()).Returns("fp-123");

        IReadOnlyList<TextLayer> layers = [TextLayer.Empty(0), TextLayer.Empty(1), TextLayer.Empty(2)];
        var ocr = Substitute.For<IOcrPipelineService>();
        ocr.RecognizeDocumentAsync(
                Arg.Any<IDocument>(),
                "fp-123",
                Arg.Any<OcrOptions>(),
                Arg.Any<IProgress<OcrProgress>>(),
                Arg.Any<CancellationToken>())
            .Returns(layers);

        var vm = CreateVm(ocr, fingerprint);

        await vm.RunOcrCommand.ExecuteAsync(null);

        vm.IsOcrRunning.Should().BeFalse();
        vm.OcrLayers.Should().BeSameAs(layers);
        vm.OcrStatus.Should().Be("OCR: 3 pages");
        await ocr.Received(1).RecognizeDocumentAsync(
            Arg.Any<IDocument>(), "fp-123", Arg.Any<OcrOptions>(),
            Arg.Any<IProgress<OcrProgress>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunOcr_WhenPipelineThrows_SetsFailedStatus_DoesNotThrow()
    {
        var fingerprint = Substitute.For<IFileFingerprint>();
        fingerprint.ComputeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("fp");

        var ocr = Substitute.For<IOcrPipelineService>();
        ocr.RecognizeDocumentAsync(
                Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<OcrOptions>(),
                Arg.Any<IProgress<OcrProgress>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<TextLayer>>(new InvalidOperationException("engine boom")));

        var vm = CreateVm(ocr, fingerprint);

        await vm.RunOcrCommand.ExecuteAsync(null);

        vm.IsOcrRunning.Should().BeFalse();
        vm.OcrStatus.Should().Be("OCR failed");
    }
}
