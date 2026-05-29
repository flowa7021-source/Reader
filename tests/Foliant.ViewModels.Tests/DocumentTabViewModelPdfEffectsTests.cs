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
        IHeaderFooterService? headerFooter = null)
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
            headerFooterService: headerFooter);
    }

    private static WatermarkSpec SampleWatermark() =>
        new("DRAFT", FontSize: 48, Opacity: 0.3, AngleDegrees: 45, R: 128, G: 128, B: 128);

    private static HeaderFooterSpec SampleHeaderFooter() =>
        new(HeaderText: "Doc", FooterText: "{page}/{total}", FontSize: 10, R: 64, G: 64, B: 64);

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
}
