using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelPdfEffectsTests
{
    private static DocumentTabViewModel CreateVm(
        string filePath = "/tmp/x.pdf",
        IWatermarkService? watermark = null,
        IHeaderFooterService? headerFooter = null,
        IPdfCropService? crop = null,
        IRedactionService? redaction = null,
        IFindAndRedactService? findAndRedact = null,
        IPdfSplitService? split = null,
        IBatesNumberingService? bates = null)
    {
        var document = Substitute.For<IDocument>();
        document.PageCount.Returns(3);
        document.Metadata.Returns(new DocumentMetadata(null, null, null, null, null, new Dictionary<string, string>()));

        var search = Substitute.For<ISearchService>();
        search.SearchInDocumentAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<IReadOnlyList<SearchHit>>([]));
        var ann = Substitute.For<IAnnotationService>();
        ann.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Annotation>>([]));
        var bm = Substitute.For<IBookmarkService>();
        bm.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([]));

        return new DocumentTabViewModel(
            document, filePath, search, ann, bm,
            NullLogger<DocumentTabViewModel>.Instance,
            watermarkService: watermark,
            headerFooterService: headerFooter,
            cropService: crop,
            redactionService: redaction,
            findAndRedactService: findAndRedact,
            splitService: split,
            batesService: bates);
    }

    private static WatermarkSpec SampleWatermark() =>
        new("DRAFT", FontSize: 48, Opacity: 0.3, AngleDegrees: 45, R: 128, G: 128, B: 128);

    private static HeaderFooterSpec SampleHeaderFooter() =>
        HeaderFooterSpec.FromCenterTexts(headerText: "Doc", footerText: "{page}/{total}", fontSize: 10, r: 64, g: 64, b: 64);

    private static CropSpec SampleCrop() => new(Left: 0.05, Top: 0.10, Right: 0.05, Bottom: 0.10);

    private static BatesNumberingSpec SampleBates() =>
        new(Prefix: "ACME-", Suffix: "", StartNumber: 1, Digits: 6,
            Position: BatesPosition.BottomRight, FontSize: 10, R: 0, G: 0, B: 0);

    // ───── CanAddWatermark / CanAddHeaderFooter gates ─────

    [Fact]
    public void CanAddWatermark_NoService_False()
    {
        var vm = CreateVm(watermark: null);
        vm.CanAddWatermark.Should().BeFalse();
    }

    [Fact]
    public void CanAddWatermark_NonPdfSource_False()
    {
        var vm = CreateVm(filePath: "/tmp/foo.djvu", watermark: Substitute.For<IWatermarkService>());
        vm.CanAddWatermark.Should().BeFalse();
    }

    [Fact]
    public void CanAddWatermark_PdfSourceAndService_True()
    {
        var vm = CreateVm(filePath: "/tmp/foo.PDF", watermark: Substitute.For<IWatermarkService>());
        vm.CanAddWatermark.Should().BeTrue();
    }

    [Fact]
    public void CanAddHeaderFooter_NoService_False()
    {
        var vm = CreateVm(headerFooter: null);
        vm.CanAddHeaderFooter.Should().BeFalse();
    }

    [Fact]
    public void CanAddHeaderFooter_PdfSourceAndService_True()
    {
        var vm = CreateVm(filePath: "/tmp/foo.pdf", headerFooter: Substitute.For<IHeaderFooterService>());
        vm.CanAddHeaderFooter.Should().BeTrue();
    }

    // ───── ApplyWatermarkCommand ─────

    [Fact]
    public async Task ApplyWatermark_ForwardsSpecAndPath_ToService()
    {
        var svc = Substitute.For<IWatermarkService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", watermark: svc);

        var spec = SampleWatermark();
        await vm.ApplyWatermarkCommand.ExecuteAsync(new ApplyWatermarkRequest(spec, "/tmp/out.pdf"));

        await svc.Received(1).ApplyAsync("/tmp/in.pdf", spec, "/tmp/out.pdf", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyWatermark_NullRequest_IsNoOp()
    {
        var svc = Substitute.For<IWatermarkService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", watermark: svc);

        await vm.ApplyWatermarkCommand.ExecuteAsync(null);

        await svc.DidNotReceiveWithAnyArgs().ApplyAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task ApplyWatermark_BlankTargetPath_IsNoOp()
    {
        var svc = Substitute.For<IWatermarkService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", watermark: svc);

        await vm.ApplyWatermarkCommand.ExecuteAsync(new ApplyWatermarkRequest(SampleWatermark(), "   "));

        await svc.DidNotReceiveWithAnyArgs().ApplyAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task ApplyWatermark_ServiceThrows_DoesNotPropagate()
    {
        var svc = Substitute.For<IWatermarkService>();
        svc.ApplyAsync(Arg.Any<string>(), Arg.Any<WatermarkSpec>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException(new InvalidOperationException("boom")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", watermark: svc);

        Func<Task> act = async () =>
            await vm.ApplyWatermarkCommand.ExecuteAsync(new ApplyWatermarkRequest(SampleWatermark(), "/tmp/out.pdf"));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ApplyWatermark_NonPdfSource_IsNoOp()
    {
        var svc = Substitute.For<IWatermarkService>();
        var vm = CreateVm(filePath: "/tmp/in.png", watermark: svc);

        await vm.ApplyWatermarkCommand.ExecuteAsync(new ApplyWatermarkRequest(SampleWatermark(), "/tmp/out.pdf"));

        await svc.DidNotReceiveWithAnyArgs().ApplyAsync(default!, default!, default!, default);
    }

    // ───── ApplyHeaderFooterCommand ─────

    [Fact]
    public async Task ApplyHeaderFooter_ForwardsSpecAndPath_ToService()
    {
        var svc = Substitute.For<IHeaderFooterService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", headerFooter: svc);

        var spec = SampleHeaderFooter();
        await vm.ApplyHeaderFooterCommand.ExecuteAsync(new ApplyHeaderFooterRequest(spec, "/tmp/out.pdf"));

        await svc.Received(1).ApplyAsync("/tmp/in.pdf", spec, "/tmp/out.pdf", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyHeaderFooter_NullRequest_IsNoOp()
    {
        var svc = Substitute.For<IHeaderFooterService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", headerFooter: svc);

        await vm.ApplyHeaderFooterCommand.ExecuteAsync(null);

        await svc.DidNotReceiveWithAnyArgs().ApplyAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task ApplyHeaderFooter_ServiceThrows_DoesNotPropagate()
    {
        var svc = Substitute.For<IHeaderFooterService>();
        svc.ApplyAsync(Arg.Any<string>(), Arg.Any<HeaderFooterSpec>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException(new IOException("disk full")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", headerFooter: svc);

        Func<Task> act = async () =>
            await vm.ApplyHeaderFooterCommand.ExecuteAsync(new ApplyHeaderFooterRequest(SampleHeaderFooter(), "/tmp/out.pdf"));

        await act.Should().NotThrowAsync();
    }

    // ───── CropPagesCommand ─────

    [Fact]
    public void CanCropPages_NoService_False()
    {
        var vm = CreateVm(crop: null);
        vm.CanCropPages.Should().BeFalse();
    }

    [Fact]
    public void CanCropPages_NonPdfSource_False()
    {
        var vm = CreateVm(filePath: "/tmp/foo.djvu", crop: Substitute.For<IPdfCropService>());
        vm.CanCropPages.Should().BeFalse();
    }

    [Fact]
    public void CanCropPages_PdfSourceAndService_True()
    {
        var vm = CreateVm(filePath: "/tmp/foo.PDF", crop: Substitute.For<IPdfCropService>());
        vm.CanCropPages.Should().BeTrue();
    }

    [Fact]
    public async Task CropPages_ForwardsSpecAndPath_ToService()
    {
        var svc = Substitute.For<IPdfCropService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", crop: svc);

        var spec = SampleCrop();
        await vm.CropPagesCommand.ExecuteAsync(new CropPagesRequest(spec, "/tmp/out.pdf"));

        await svc.Received(1).ApplyAsync("/tmp/in.pdf", spec, "/tmp/out.pdf", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CropPages_NullRequest_IsNoOp()
    {
        var svc = Substitute.For<IPdfCropService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", crop: svc);

        await vm.CropPagesCommand.ExecuteAsync(null);

        await svc.DidNotReceiveWithAnyArgs().ApplyAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task CropPages_BlankTargetPath_IsNoOp()
    {
        var svc = Substitute.For<IPdfCropService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", crop: svc);

        await vm.CropPagesCommand.ExecuteAsync(new CropPagesRequest(SampleCrop(), "  "));

        await svc.DidNotReceiveWithAnyArgs().ApplyAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task CropPages_NoEffectSpec_IsNoOp()
    {
        var svc = Substitute.For<IPdfCropService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", crop: svc);

        await vm.CropPagesCommand.ExecuteAsync(new CropPagesRequest(new CropSpec(0, 0, 0, 0), "/tmp/out.pdf"));

        await svc.DidNotReceiveWithAnyArgs().ApplyAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task CropPages_ServiceThrows_DoesNotPropagate()
    {
        var svc = Substitute.For<IPdfCropService>();
        svc.ApplyAsync(Arg.Any<string>(), Arg.Any<CropSpec>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException(new InvalidOperationException("boom")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", crop: svc);

        Func<Task> act = async () =>
            await vm.CropPagesCommand.ExecuteAsync(new CropPagesRequest(SampleCrop(), "/tmp/out.pdf"));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CropPages_NonPdfSource_IsNoOp()
    {
        var svc = Substitute.For<IPdfCropService>();
        var vm = CreateVm(filePath: "/tmp/in.png", crop: svc);

        await vm.CropPagesCommand.ExecuteAsync(new CropPagesRequest(SampleCrop(), "/tmp/out.pdf"));

        await svc.DidNotReceiveWithAnyArgs().ApplyAsync(default!, default!, default!, default);
    }

    // ───── RedactPagesCommand / FindAndRedactCommand ─────

    private static IReadOnlyList<RedactionRegion> SampleRegions() =>
    [
        new(PageIndex: 0, Rect: new AnnotationRect(100, 200, 50, 12)),
        new(PageIndex: 1, Rect: new AnnotationRect(120, 220, 80, 12)),
    ];

    private static FindAndRedactOptions SampleOptions() =>
        new(CaseSensitive: true, WholeWord: false, Regex: true, FoldDiacritics: false);

    [Fact]
    public void CanRedactPages_NoServices_False()
    {
        var vm = CreateVm(redaction: null, findAndRedact: null);
        vm.CanRedactPages.Should().BeFalse();
    }

    [Fact]
    public void CanRedactPages_NonPdfSource_False()
    {
        var vm = CreateVm(filePath: "/tmp/foo.djvu",
            redaction: Substitute.For<IRedactionService>(),
            findAndRedact: Substitute.For<IFindAndRedactService>());
        vm.CanRedactPages.Should().BeFalse();
    }

    [Fact]
    public void CanRedactPages_PdfSourceAndRedactionService_True()
    {
        var vm = CreateVm(filePath: "/tmp/foo.PDF", redaction: Substitute.For<IRedactionService>());
        vm.CanRedactPages.Should().BeTrue();
    }

    [Fact]
    public async Task RedactPagesCommand_ForwardsRegionsAndPath_ToService()
    {
        var svc = Substitute.For<IRedactionService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", redaction: svc);

        var regions = SampleRegions();
        await vm.RedactPagesCommand.ExecuteAsync(new RedactPagesRequest(regions, "/tmp/out.pdf"));

        await svc.Received(1).RedactAsync("/tmp/in.pdf", "/tmp/out.pdf", regions, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RedactPagesCommand_NullRequest_IsNoOp()
    {
        var svc = Substitute.For<IRedactionService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", redaction: svc);

        await vm.RedactPagesCommand.ExecuteAsync(null);

        await svc.DidNotReceiveWithAnyArgs().RedactAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task RedactPagesCommand_EmptyRegions_IsNoOp()
    {
        var svc = Substitute.For<IRedactionService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", redaction: svc);

        await vm.RedactPagesCommand.ExecuteAsync(new RedactPagesRequest([], "/tmp/out.pdf"));

        await svc.DidNotReceiveWithAnyArgs().RedactAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task RedactPagesCommand_ServiceThrows_DoesNotPropagate()
    {
        var svc = Substitute.For<IRedactionService>();
        svc.RedactAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<RedactionRegion>>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException(new InvalidOperationException("boom")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", redaction: svc);

        Func<Task> act = async () =>
            await vm.RedactPagesCommand.ExecuteAsync(new RedactPagesRequest(SampleRegions(), "/tmp/out.pdf"));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task FindAndRedactCommand_ForwardsQueryAndOptions_ToService()
    {
        var svc = Substitute.For<IFindAndRedactService>();
        svc.RedactMatchesAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<FindAndRedactOptions>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult(2));
        var vm = CreateVm(filePath: "/tmp/in.pdf", findAndRedact: svc);

        var opts = SampleOptions();
        await vm.FindAndRedactCommand.ExecuteAsync(new FindAndRedactRequest("secret", opts, "/tmp/out.pdf"));

        await svc.Received(1).RedactMatchesAsync(
            Arg.Any<IDocument>(),
            "/tmp/in.pdf",
            "/tmp/out.pdf",
            "secret",
            opts,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FindAndRedactCommand_NullRequest_IsNoOp()
    {
        var svc = Substitute.For<IFindAndRedactService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", findAndRedact: svc);

        await vm.FindAndRedactCommand.ExecuteAsync(null);

        await svc.DidNotReceiveWithAnyArgs().RedactMatchesAsync(default!, default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task FindAndRedactCommand_BlankQuery_IsNoOp()
    {
        var svc = Substitute.For<IFindAndRedactService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", findAndRedact: svc);

        await vm.FindAndRedactCommand.ExecuteAsync(new FindAndRedactRequest("  ", new FindAndRedactOptions(), "/tmp/out.pdf"));

        await svc.DidNotReceiveWithAnyArgs().RedactMatchesAsync(default!, default!, default!, default!, default!, default);
    }

    [Fact]
    public void FindAndRedactCommand_OnNonPdfDocument_DoesNotExecute()
    {
        var svc = Substitute.For<IFindAndRedactService>();
        var vm = CreateVm(filePath: "/tmp/in.png", findAndRedact: svc);

        vm.FindAndRedactCommand.CanExecute(new FindAndRedactRequest("x", new FindAndRedactOptions(), "/tmp/out.pdf"))
            .Should().BeFalse();
    }

    [Fact]
    public void FindAndRedactCommand_ServiceNotRegistered_NoOp()
    {
        var vm = CreateVm(filePath: "/tmp/in.pdf", findAndRedact: null);

        vm.CanRedactPages.Should().BeFalse();
        vm.FindAndRedactCommand.CanExecute(new FindAndRedactRequest("x", new FindAndRedactOptions(), "/tmp/out.pdf"))
            .Should().BeFalse();
    }

    [Fact]
    public async Task FindAndRedactCommand_ServiceThrows_DoesNotPropagate()
    {
        var svc = Substitute.For<IFindAndRedactService>();
        svc.RedactMatchesAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<FindAndRedactOptions>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException<int>(new InvalidOperationException("bad regex")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", findAndRedact: svc);

        Func<Task> act = async () =>
            await vm.FindAndRedactCommand.ExecuteAsync(new FindAndRedactRequest("x", new FindAndRedactOptions(), "/tmp/out.pdf"));

        await act.Should().NotThrowAsync();
    }

    // ───── CanApplyBates gate / ApplyBatesCommand ─────

    [Fact]
    public void CanApplyBates_NoService_False()
    {
        var vm = CreateVm(bates: null);
        vm.CanApplyBates.Should().BeFalse();
    }

    [Fact]
    public void CanApplyBates_PdfSourceAndService_True()
    {
        var vm = CreateVm(filePath: "/tmp/foo.PDF", bates: Substitute.For<IBatesNumberingService>());
        vm.CanApplyBates.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyBatesCommand_ForwardsSpecAndPath_ToService()
    {
        var svc = Substitute.For<IBatesNumberingService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", bates: svc);

        var spec = SampleBates();
        await vm.ApplyBatesCommand.ExecuteAsync(new ApplyBatesRequest(spec, "/tmp/out.pdf"));

        await svc.Received(1).ApplyAsync("/tmp/in.pdf", spec, "/tmp/out.pdf", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ApplyBatesCommand_OnNonPdfDocument_DoesNotExecute()
    {
        var svc = Substitute.For<IBatesNumberingService>();
        var vm = CreateVm(filePath: "/tmp/in.png", bates: svc);

        vm.ApplyBatesCommand.CanExecute(new ApplyBatesRequest(SampleBates(), "/tmp/out.pdf"))
            .Should().BeFalse();
    }

    [Fact]
    public void ApplyBatesCommand_ServiceNotRegistered_NoOp()
    {
        var vm = CreateVm(filePath: "/tmp/in.pdf", bates: null);

        vm.CanApplyBates.Should().BeFalse();
        vm.ApplyBatesCommand.CanExecute(new ApplyBatesRequest(SampleBates(), "/tmp/out.pdf"))
            .Should().BeFalse();
    }

    [Fact]
    public async Task ApplyBatesCommand_NullRequest_IsNoOp()
    {
        var svc = Substitute.For<IBatesNumberingService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", bates: svc);

        await vm.ApplyBatesCommand.ExecuteAsync(null);

        await svc.DidNotReceiveWithAnyArgs().ApplyAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task ApplyBatesCommand_BlankTargetPath_IsNoOp()
    {
        var svc = Substitute.For<IBatesNumberingService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", bates: svc);

        await vm.ApplyBatesCommand.ExecuteAsync(new ApplyBatesRequest(SampleBates(), "  "));

        await svc.DidNotReceiveWithAnyArgs().ApplyAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task ApplyBatesCommand_ServiceThrows_DoesNotPropagate()
    {
        var svc = Substitute.For<IBatesNumberingService>();
        svc.ApplyAsync(Arg.Any<string>(), Arg.Any<BatesNumberingSpec>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException(new InvalidOperationException("boom")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", bates: svc);

        Func<Task> act = async () =>
            await vm.ApplyBatesCommand.ExecuteAsync(new ApplyBatesRequest(SampleBates(), "/tmp/out.pdf"));

        await act.Should().NotThrowAsync();
    }

    // ───── CanSplitPdf gate / SplitEveryCommand / ExtractSelectionCommand ─────

    [Fact]
    public void CanSplitPdf_NoService_False()
    {
        var vm = CreateVm(split: null);
        vm.CanSplitPdf.Should().BeFalse();
    }

    [Fact]
    public void CanSplitPdf_NonPdfSource_False()
    {
        var vm = CreateVm(filePath: "/tmp/foo.djvu", split: Substitute.For<IPdfSplitService>());
        vm.CanSplitPdf.Should().BeFalse();
    }

    [Fact]
    public void CanSplitPdf_PdfSourceAndService_True()
    {
        var vm = CreateVm(filePath: "/tmp/foo.PDF", split: Substitute.For<IPdfSplitService>());
        vm.CanSplitPdf.Should().BeTrue();
    }

    [Fact]
    public async Task SplitEveryCommand_ForwardsArgs_ToService()
    {
        var svc = Substitute.For<IPdfSplitService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", split: svc);

        await vm.SplitEveryCommand.ExecuteAsync(new SplitEveryRequest(5, "/tmp/out", "in"));

        await svc.Received(1).SplitEveryAsync("/tmp/in.pdf", 5, "/tmp/out", "in", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SplitEveryCommand_NullRequest_NoOp()
    {
        var svc = Substitute.For<IPdfSplitService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", split: svc);

        await vm.SplitEveryCommand.ExecuteAsync(null);

        await svc.DidNotReceiveWithAnyArgs().SplitEveryAsync(default!, default, default!, default!, default);
    }

    [Fact]
    public async Task SplitEveryCommand_NonPositiveChunk_NoOp()
    {
        var svc = Substitute.For<IPdfSplitService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", split: svc);

        await vm.SplitEveryCommand.ExecuteAsync(new SplitEveryRequest(0, "/tmp/out", "in"));

        await svc.DidNotReceiveWithAnyArgs().SplitEveryAsync(default!, default, default!, default!, default);
    }

    [Fact]
    public void SplitEveryCommand_OnNonPdf_DoesNotExecute()
    {
        var svc = Substitute.For<IPdfSplitService>();
        var vm = CreateVm(filePath: "/tmp/in.png", split: svc);

        vm.SplitEveryCommand.CanExecute(new SplitEveryRequest(5, "/tmp/out", "in")).Should().BeFalse();
    }

    [Fact]
    public void SplitEveryCommand_ServiceNull_DoesNotExecute()
    {
        var vm = CreateVm(filePath: "/tmp/in.pdf", split: null);

        vm.CanSplitPdf.Should().BeFalse();
        vm.SplitEveryCommand.CanExecute(new SplitEveryRequest(5, "/tmp/out", "in")).Should().BeFalse();
    }

    [Fact]
    public async Task SplitEveryCommand_ServiceThrows_DoesNotPropagate()
    {
        var svc = Substitute.For<IPdfSplitService>();
        svc.SplitEveryAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException<IReadOnlyList<string>>(new InvalidOperationException("boom")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", split: svc);

        Func<Task> act = async () =>
            await vm.SplitEveryCommand.ExecuteAsync(new SplitEveryRequest(5, "/tmp/out", "in"));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExtractSelectionCommand_ForwardsArgs_ToService()
    {
        var svc = Substitute.For<IPdfSplitService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", split: svc);

        IReadOnlyList<int> pages = [0, 2, 6];
        await vm.ExtractSelectionCommand.ExecuteAsync(new ExtractSelectionRequest(pages, "/tmp/out.pdf"));

        await svc.Received(1).ExtractSelectionAsync("/tmp/in.pdf", pages, "/tmp/out.pdf", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractSelectionCommand_NullRequest_NoOp()
    {
        var svc = Substitute.For<IPdfSplitService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", split: svc);

        await vm.ExtractSelectionCommand.ExecuteAsync(null);

        await svc.DidNotReceiveWithAnyArgs().ExtractSelectionAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task ExtractSelectionCommand_EmptySelection_NoOp()
    {
        var svc = Substitute.For<IPdfSplitService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", split: svc);

        await vm.ExtractSelectionCommand.ExecuteAsync(new ExtractSelectionRequest([], "/tmp/out.pdf"));

        await svc.DidNotReceiveWithAnyArgs().ExtractSelectionAsync(default!, default!, default!, default);
    }

    [Fact]
    public void ExtractSelectionCommand_OnNonPdf_DoesNotExecute()
    {
        var svc = Substitute.For<IPdfSplitService>();
        var vm = CreateVm(filePath: "/tmp/in.png", split: svc);

        vm.ExtractSelectionCommand.CanExecute(new ExtractSelectionRequest([0, 1], "/tmp/out.pdf")).Should().BeFalse();
    }

    [Fact]
    public async Task ExtractSelectionCommand_ServiceThrows_DoesNotPropagate()
    {
        var svc = Substitute.For<IPdfSplitService>();
        svc.ExtractSelectionAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<int>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException(new InvalidOperationException("boom")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", split: svc);

        Func<Task> act = async () =>
            await vm.ExtractSelectionCommand.ExecuteAsync(new ExtractSelectionRequest([0, 1], "/tmp/out.pdf"));

        await act.Should().NotThrowAsync();
    }
}
