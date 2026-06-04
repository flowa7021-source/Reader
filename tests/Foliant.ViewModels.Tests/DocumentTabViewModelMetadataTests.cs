using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelMetadataTests
{
    private static readonly DocumentMetadata SampleMetadata = new(
        Title: "Old Title",
        Author: "Old Author",
        Subject: "Old Subject",
        Created: new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero),
        Modified: new DateTimeOffset(2025, 6, 7, 8, 9, 10, TimeSpan.Zero),
        Custom: new Dictionary<string, string>());

    private static DocumentTabViewModel CreateVm(
        string filePath = "/tmp/x.pdf",
        IPdfMetadataEditService? metadata = null,
        DocumentMetadata? documentMetadata = null)
    {
        var document = Substitute.For<IDocument>();
        document.PageCount.Returns(3);
        document.Metadata.Returns(documentMetadata ?? SampleMetadata);

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
            metadataEditService: metadata);
    }

    private static PdfMetadataSpec SampleSpec() =>
        new(Title: "New Title", Author: "New Author", Subject: "New Subject",
            Keywords: "k1, k2", Creator: "Foliant", Producer: "Foliant");

    // ───── CanEditMetadata gate ─────

    [Fact]
    public void CanEditMetadata_NoService_False()
    {
        var vm = CreateVm(metadata: null);
        vm.CanEditMetadata.Should().BeFalse();
    }

    [Fact]
    public void CanEditMetadata_NonPdfSource_False()
    {
        var vm = CreateVm(filePath: "/tmp/foo.djvu", metadata: Substitute.For<IPdfMetadataEditService>());
        vm.CanEditMetadata.Should().BeFalse();
    }

    [Fact]
    public void CanEditMetadata_PdfSourceAndService_True()
    {
        var vm = CreateVm(filePath: "/tmp/foo.PDF", metadata: Substitute.For<IPdfMetadataEditService>());
        vm.CanEditMetadata.Should().BeTrue();
    }

    // ───── CurrentMetadata snapshot ─────

    [Fact]
    public void CurrentMetadata_ReturnsDocumentMetadata()
    {
        var vm = CreateVm(documentMetadata: SampleMetadata);
        vm.CurrentMetadata.Should().BeSameAs(SampleMetadata);
    }

    // ───── SaveMetadataCommand ─────

    [Fact]
    public async Task SaveMetadataCommand_ForwardsSpecAndPath_ToService()
    {
        var svc = Substitute.For<IPdfMetadataEditService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", metadata: svc);

        var spec = SampleSpec();
        await vm.SaveMetadataCommand.ExecuteAsync(new SaveMetadataRequest(spec, "/tmp/out.pdf"));

        await svc.Received(1).EditAsync("/tmp/in.pdf", "/tmp/out.pdf", spec, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SaveMetadataCommand_OnNonPdf_DoesNotExecute()
    {
        var svc = Substitute.For<IPdfMetadataEditService>();
        var vm = CreateVm(filePath: "/tmp/in.png", metadata: svc);

        vm.SaveMetadataCommand.CanExecute(new SaveMetadataRequest(SampleSpec(), "/tmp/out.pdf"))
            .Should().BeFalse();
    }

    [Fact]
    public void SaveMetadataCommand_ServiceNull_DoesNotExecute()
    {
        var vm = CreateVm(filePath: "/tmp/in.pdf", metadata: null);

        vm.CanEditMetadata.Should().BeFalse();
        vm.SaveMetadataCommand.CanExecute(new SaveMetadataRequest(SampleSpec(), "/tmp/out.pdf"))
            .Should().BeFalse();
    }

    [Fact]
    public async Task SaveMetadataCommand_NullRequest_NoOp()
    {
        var svc = Substitute.For<IPdfMetadataEditService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", metadata: svc);

        await vm.SaveMetadataCommand.ExecuteAsync(null);

        await svc.DidNotReceiveWithAnyArgs().EditAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task SaveMetadataCommand_BlankTargetPath_NoOp()
    {
        var svc = Substitute.For<IPdfMetadataEditService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", metadata: svc);

        await vm.SaveMetadataCommand.ExecuteAsync(new SaveMetadataRequest(SampleSpec(), "   "));

        await svc.DidNotReceiveWithAnyArgs().EditAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task SaveMetadataCommand_ServiceThrows_DoesNotPropagate()
    {
        var svc = Substitute.For<IPdfMetadataEditService>();
        svc.EditAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<PdfMetadataSpec>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException(new InvalidOperationException("boom")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", metadata: svc);

        Func<Task> act = async () =>
            await vm.SaveMetadataCommand.ExecuteAsync(new SaveMetadataRequest(SampleSpec(), "/tmp/out.pdf"));

        await act.Should().NotThrowAsync();
    }
}
