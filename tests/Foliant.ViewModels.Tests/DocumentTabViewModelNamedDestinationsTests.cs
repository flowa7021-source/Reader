using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelNamedDestinationsTests
{
    private static readonly DocumentMetadata SampleMetadata = new(
        Title: "t", Author: "a", Subject: "s",
        Created: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Modified: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Custom: new Dictionary<string, string>());

    private static DocumentTabViewModel CreateVm(string filePath = "/tmp/x.pdf", IPdfNamedDestinationService? svc = null)
    {
        var document = Substitute.For<IDocument>();
        document.PageCount.Returns(3);
        document.Metadata.Returns(SampleMetadata);
        var search = Substitute.For<ISearchService>();
        search.SearchInDocumentAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<IReadOnlyList<SearchHit>>([]));
        var ann = Substitute.For<IAnnotationService>();
        ann.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Annotation>>([]));
        var bm = Substitute.For<IBookmarkService>();
        bm.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Bookmark>>([]));
        return new DocumentTabViewModel(document, filePath, search, ann, bm,
            NullLogger<DocumentTabViewModel>.Instance, namedDestinationService: svc);
    }

    private static IReadOnlyList<PdfNamedDestination> Sample() =>
        [new PdfNamedDestination("appendix", 7), new PdfNamedDestination("intro", 1)];

    [Fact]
    public void CanManageNamedDestinations_NoService_False() => CreateVm(svc: null).CanManageNamedDestinations.Should().BeFalse();

    [Fact]
    public void CanManageNamedDestinations_NonPdf_False() =>
        CreateVm(filePath: "/tmp/f.epub", svc: Substitute.For<IPdfNamedDestinationService>()).CanManageNamedDestinations.Should().BeFalse();

    [Fact]
    public void CanManageNamedDestinations_PdfAndService_True() =>
        CreateVm(filePath: "/tmp/f.PDF", svc: Substitute.For<IPdfNamedDestinationService>()).CanManageNamedDestinations.Should().BeTrue();

    [Fact]
    public async Task LoadNamedDestinationsCommand_PopulatesSnapshot()
    {
        var svc = Substitute.For<IPdfNamedDestinationService>();
        svc.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(Sample()));
        var vm = CreateVm(filePath: "/tmp/in.pdf", svc: svc);

        await vm.LoadNamedDestinationsCommand.ExecuteAsync(null);

        vm.CurrentNamedDestinations.Should().Equal(Sample());
    }

    [Fact]
    public async Task LoadNamedDestinationsCommand_ServiceThrows_LeavesEmpty()
    {
        var svc = Substitute.For<IPdfNamedDestinationService>();
        svc.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException<IReadOnlyList<PdfNamedDestination>>(new InvalidOperationException("boom")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", svc: svc);

        Func<Task> act = async () => await vm.LoadNamedDestinationsCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync();
        vm.CurrentNamedDestinations.Should().BeEmpty();
    }

    [Fact]
    public async Task AddNamedDestinationCommand_ForwardsArgs()
    {
        var svc = Substitute.For<IPdfNamedDestinationService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", svc: svc);

        await vm.AddNamedDestinationCommand.ExecuteAsync(new AddNamedDestinationRequest("intro", 2, "/tmp/out.pdf"));

        await svc.Received(1).AddAsync("/tmp/in.pdf", "/tmp/out.pdf", "intro", 2, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AddNamedDestinationCommand_NonPdf_CannotExecute() =>
        CreateVm(filePath: "/tmp/in.png", svc: Substitute.For<IPdfNamedDestinationService>())
            .AddNamedDestinationCommand.CanExecute(new AddNamedDestinationRequest("a", 0, "/o.pdf")).Should().BeFalse();

    [Fact]
    public async Task AddNamedDestinationCommand_BlankArgs_NoOp()
    {
        var svc = Substitute.For<IPdfNamedDestinationService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", svc: svc);

        await vm.AddNamedDestinationCommand.ExecuteAsync(new AddNamedDestinationRequest("  ", 0, "/o.pdf"));
        await vm.AddNamedDestinationCommand.ExecuteAsync(new AddNamedDestinationRequest("a", 0, "  "));

        await svc.DidNotReceiveWithAnyArgs().AddAsync(default!, default!, default!, default, default);
    }

    [Fact]
    public async Task RemoveNamedDestinationCommand_ForwardsArgs()
    {
        var svc = Substitute.For<IPdfNamedDestinationService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", svc: svc);

        await vm.RemoveNamedDestinationCommand.ExecuteAsync(new RemoveNamedDestinationRequest("intro", "/tmp/out.pdf"));

        await svc.Received(1).RemoveAsync("/tmp/in.pdf", "/tmp/out.pdf", "intro", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveNamedDestinationCommand_BlankArgs_NoOp()
    {
        var svc = Substitute.For<IPdfNamedDestinationService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", svc: svc);

        await vm.RemoveNamedDestinationCommand.ExecuteAsync(new RemoveNamedDestinationRequest("  ", "/o.pdf"));

        await svc.DidNotReceiveWithAnyArgs().RemoveAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task AddNamedDestinationCommand_ServiceThrows_DoesNotPropagate()
    {
        var svc = Substitute.For<IPdfNamedDestinationService>();
        svc.AddAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException(new InvalidOperationException("boom")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", svc: svc);

        Func<Task> act = async () => await vm.AddNamedDestinationCommand.ExecuteAsync(new AddNamedDestinationRequest("a", 0, "/o.pdf"));

        await act.Should().NotThrowAsync();
    }
}
