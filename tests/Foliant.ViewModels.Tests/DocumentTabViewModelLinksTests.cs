using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelLinksTests
{
    private static readonly DocumentMetadata SampleMetadata = new(
        Title: "t", Author: "a", Subject: "s",
        Created: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Modified: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Custom: new Dictionary<string, string>());

    private static DocumentTabViewModel CreateVm(string filePath = "/tmp/x.pdf", IPdfLinkService? links = null)
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
            NullLogger<DocumentTabViewModel>.Instance, linkService: links);
    }

    [Fact]
    public void CanListLinks_NoService_False() => CreateVm(links: null).CanListLinks.Should().BeFalse();

    [Fact]
    public void CanListLinks_NonPdf_False() =>
        CreateVm(filePath: "/tmp/f.epub", links: Substitute.For<IPdfLinkService>()).CanListLinks.Should().BeFalse();

    [Fact]
    public void CanListLinks_PdfAndService_True() =>
        CreateVm(filePath: "/tmp/f.PDF", links: Substitute.For<IPdfLinkService>()).CanListLinks.Should().BeTrue();

    [Fact]
    public async Task LoadLinksCommand_PopulatesCurrentLinks()
    {
        var svc = Substitute.For<IPdfLinkService>();
        IReadOnlyList<PdfLinkAnnotation> sample =
            [new PdfLinkAnnotation(0, "https://example.com", null), new PdfLinkAnnotation(0, null, 2)];
        svc.ListLinksAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(sample));
        var vm = CreateVm(filePath: "/tmp/in.pdf", links: svc);

        await vm.LoadLinksCommand.ExecuteAsync(null);

        vm.CurrentLinks.Should().Equal(sample);
    }

    [Fact]
    public async Task LoadLinksCommand_ServiceThrows_LeavesEmpty()
    {
        var svc = Substitute.For<IPdfLinkService>();
        svc.ListLinksAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException<IReadOnlyList<PdfLinkAnnotation>>(new InvalidOperationException("boom")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", links: svc);

        Func<Task> act = async () => await vm.LoadLinksCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync();
        vm.CurrentLinks.Should().BeEmpty();
    }
}
