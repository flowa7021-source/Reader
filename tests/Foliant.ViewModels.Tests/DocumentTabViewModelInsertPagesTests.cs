using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelInsertPagesTests
{
    private static readonly DocumentMetadata SampleMetadata = new(
        Title: "t", Author: "a", Subject: "s",
        Created: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Modified: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Custom: new Dictionary<string, string>());

    private static DocumentTabViewModel CreateVm(
        string filePath = "/tmp/x.pdf",
        IPdfInsertPagesService? insert = null)
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
            insertPagesService: insert);
    }

    [Fact]
    public void CanInsertPages_NoService_False() =>
        CreateVm(insert: null).CanInsertPages.Should().BeFalse();

    [Fact]
    public void CanInsertPages_NonPdfSource_False() =>
        CreateVm(filePath: "/tmp/foo.djvu", insert: Substitute.For<IPdfInsertPagesService>())
            .CanInsertPages.Should().BeFalse();

    [Fact]
    public void CanInsertPages_PdfSourceAndService_True() =>
        CreateVm(filePath: "/tmp/foo.PDF", insert: Substitute.For<IPdfInsertPagesService>())
            .CanInsertPages.Should().BeTrue();

    [Fact]
    public async Task InsertPagesCommand_ForwardsArgs()
    {
        var svc = Substitute.For<IPdfInsertPagesService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", insert: svc);

        await vm.InsertPagesCommand.ExecuteAsync(new InsertPagesRequest(2, "/tmp/extra.pdf", "/tmp/out.pdf"));

        await svc.Received(1).InsertAsync("/tmp/in.pdf", 2, "/tmp/extra.pdf", "/tmp/out.pdf", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void InsertPagesCommand_NonPdf_CannotExecute() =>
        CreateVm(filePath: "/tmp/in.png", insert: Substitute.For<IPdfInsertPagesService>())
            .InsertPagesCommand.CanExecute(new InsertPagesRequest(0, "/e.pdf", "/o.pdf"))
            .Should().BeFalse();

    [Fact]
    public void InsertPagesCommand_ServiceNull_CannotExecute() =>
        CreateVm(filePath: "/tmp/in.pdf", insert: null)
            .InsertPagesCommand.CanExecute(new InsertPagesRequest(0, "/e.pdf", "/o.pdf"))
            .Should().BeFalse();

    [Fact]
    public async Task InsertPagesCommand_NullRequest_NoOp()
    {
        var svc = Substitute.For<IPdfInsertPagesService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", insert: svc);

        await vm.InsertPagesCommand.ExecuteAsync(null);

        await svc.DidNotReceiveWithAnyArgs().InsertAsync(default!, default, default!, default!, default);
    }

    [Fact]
    public async Task InsertPagesCommand_BlankPaths_NoOp()
    {
        var svc = Substitute.For<IPdfInsertPagesService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", insert: svc);

        await vm.InsertPagesCommand.ExecuteAsync(new InsertPagesRequest(0, "  ", "/o.pdf"));
        await vm.InsertPagesCommand.ExecuteAsync(new InsertPagesRequest(0, "/e.pdf", "  "));

        await svc.DidNotReceiveWithAnyArgs().InsertAsync(default!, default, default!, default!, default);
    }

    [Fact]
    public async Task InsertPagesCommand_ServiceThrows_DoesNotPropagate()
    {
        var svc = Substitute.For<IPdfInsertPagesService>();
        svc.InsertAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException(new InvalidOperationException("boom")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", insert: svc);

        Func<Task> act = async () =>
            await vm.InsertPagesCommand.ExecuteAsync(new InsertPagesRequest(0, "/e.pdf", "/o.pdf"));

        await act.Should().NotThrowAsync();
    }
}
