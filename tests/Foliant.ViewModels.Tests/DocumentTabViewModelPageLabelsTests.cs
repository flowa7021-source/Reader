using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelPageLabelsTests
{
    private static readonly DocumentMetadata SampleMetadata = new(
        Title: "t", Author: "a", Subject: "s",
        Created: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Modified: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Custom: new Dictionary<string, string>());

    private static DocumentTabViewModel CreateVm(
        string filePath = "/tmp/x.pdf",
        IPdfPageLabelService? pageLabels = null)
    {
        var document = Substitute.For<IDocument>();
        document.PageCount.Returns(3);
        document.Metadata.Returns(SampleMetadata);

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
            pageLabelService: pageLabels);
    }

    private static IReadOnlyList<PdfPageLabelRange> SampleRanges() =>
    [
        PdfPageLabelRange.Create(0, PdfPageLabelStyle.LowerRoman),
        PdfPageLabelRange.Create(3, PdfPageLabelStyle.Arabic),
    ];

    // ───── CanEditPageLabels gate ─────

    [Fact]
    public void CanEditPageLabels_NoService_False() =>
        CreateVm(pageLabels: null).CanEditPageLabels.Should().BeFalse();

    [Fact]
    public void CanEditPageLabels_NonPdfSource_False() =>
        CreateVm(filePath: "/tmp/foo.djvu", pageLabels: Substitute.For<IPdfPageLabelService>())
            .CanEditPageLabels.Should().BeFalse();

    [Fact]
    public void CanEditPageLabels_PdfSourceAndService_True() =>
        CreateVm(filePath: "/tmp/foo.PDF", pageLabels: Substitute.For<IPdfPageLabelService>())
            .CanEditPageLabels.Should().BeTrue();

    // ───── LoadPageLabelsCommand ─────

    [Fact]
    public async Task LoadPageLabelsCommand_PopulatesSnapshotFromService()
    {
        var svc = Substitute.For<IPdfPageLabelService>();
        svc.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult(SampleRanges()));
        var vm = CreateVm(filePath: "/tmp/in.pdf", pageLabels: svc);

        await vm.LoadPageLabelsCommand.ExecuteAsync(null);

        vm.CurrentPageLabels.Should().Equal(SampleRanges());
    }

    [Fact]
    public async Task LoadPageLabelsCommand_ServiceThrows_LeavesSnapshotEmpty()
    {
        var svc = Substitute.For<IPdfPageLabelService>();
        svc.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException<IReadOnlyList<PdfPageLabelRange>>(new InvalidOperationException("boom")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", pageLabels: svc);

        Func<Task> act = async () => await vm.LoadPageLabelsCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync();
        vm.CurrentPageLabels.Should().BeEmpty();
    }

    // ───── SavePageLabelsCommand ─────

    [Fact]
    public async Task SavePageLabelsCommand_ForwardsRangesAndPath_ToService()
    {
        var svc = Substitute.For<IPdfPageLabelService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", pageLabels: svc);
        var ranges = SampleRanges();

        await vm.SavePageLabelsCommand.ExecuteAsync(new SavePageLabelsRequest(ranges, "/tmp/out.pdf"));

        await svc.Received(1).WriteAsync("/tmp/in.pdf", "/tmp/out.pdf", ranges, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SavePageLabelsCommand_OnNonPdf_CannotExecute() =>
        CreateVm(filePath: "/tmp/in.png", pageLabels: Substitute.For<IPdfPageLabelService>())
            .SavePageLabelsCommand.CanExecute(new SavePageLabelsRequest(SampleRanges(), "/tmp/out.pdf"))
            .Should().BeFalse();

    [Fact]
    public void SavePageLabelsCommand_ServiceNull_CannotExecute() =>
        CreateVm(filePath: "/tmp/in.pdf", pageLabels: null)
            .SavePageLabelsCommand.CanExecute(new SavePageLabelsRequest(SampleRanges(), "/tmp/out.pdf"))
            .Should().BeFalse();

    [Fact]
    public async Task SavePageLabelsCommand_NullRequest_NoOp()
    {
        var svc = Substitute.For<IPdfPageLabelService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", pageLabels: svc);

        await vm.SavePageLabelsCommand.ExecuteAsync(null);

        await svc.DidNotReceiveWithAnyArgs().WriteAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task SavePageLabelsCommand_BlankTarget_NoOp()
    {
        var svc = Substitute.For<IPdfPageLabelService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", pageLabels: svc);

        await vm.SavePageLabelsCommand.ExecuteAsync(new SavePageLabelsRequest(SampleRanges(), "   "));

        await svc.DidNotReceiveWithAnyArgs().WriteAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task SavePageLabelsCommand_ServiceThrows_DoesNotPropagate()
    {
        var svc = Substitute.For<IPdfPageLabelService>();
        svc.WriteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<PdfPageLabelRange>>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException(new InvalidOperationException("boom")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", pageLabels: svc);

        Func<Task> act = async () =>
            await vm.SavePageLabelsCommand.ExecuteAsync(new SavePageLabelsRequest(SampleRanges(), "/tmp/out.pdf"));

        await act.Should().NotThrowAsync();
    }
}
