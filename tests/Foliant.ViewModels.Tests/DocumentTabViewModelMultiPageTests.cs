using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelMultiPageTests
{
    private static DocumentTabViewModel CreateVm(int pageCount = 5)
    {
        var document = Substitute.For<IDocument>();
        document.PageCount.Returns(pageCount);
        document.Metadata.Returns(new DocumentMetadata(null, null, null, null, null, new Dictionary<string, string>()));
        document.GetPageSize(Arg.Any<int>()).Returns(new PageSize(612, 792));
        document.GetTextLayerAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<TextLayer?>(null));
        document.RenderPageAsync(Arg.Any<int>(), Arg.Any<RenderOptions>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult<IPageRender>(new FakePageRender()));

        var search = Substitute.For<ISearchService>();
        search.SearchInDocumentAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<IReadOnlyList<SearchHit>>([]));
        var annotations = Substitute.For<IAnnotationService>();
        annotations.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(Task.FromResult<IReadOnlyList<Annotation>>([]));
        var bookmarks = Substitute.For<IBookmarkService>();
        bookmarks.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([]));

        return new DocumentTabViewModel(document, "/tmp/x.pdf", search, annotations, bookmarks, NullLogger<DocumentTabViewModel>.Instance);
    }

    [Fact]
    public void SinglePage_HasNoVisiblePages()
    {
        var vm = CreateVm();
        vm.VisiblePages.Should().BeEmpty();
    }

    [Fact]
    public void Continuous_BuildsLazyPlaceholdersForAllPages()
    {
        var vm = CreateVm(pageCount: 3);
        vm.SetContinuousViewCommand.Execute(null);

        vm.VisiblePages.Select(p => p.PageIndex).Should().Equal([0, 1, 2]);
        vm.VisiblePages.Should().OnlyContain(p => p.Render == null); // not rendered until realized
    }

    [Fact]
    public void TwoPage_TracksCurrentAndNext()
    {
        var vm = CreateVm(pageCount: 5);
        vm.SetTwoPageViewCommand.Execute(null);
        vm.VisiblePages.Select(p => p.PageIndex).Should().Equal([0, 1]);

        vm.CurrentPageIndex = 2;
        vm.VisiblePages.Select(p => p.PageIndex).Should().Equal([2, 3]);
    }

    [Fact]
    public void SwitchingBackToSinglePage_ClearsVisiblePages()
    {
        var vm = CreateVm();
        vm.SetContinuousViewCommand.Execute(null);
        vm.VisiblePages.Should().NotBeEmpty();

        vm.SetSinglePageViewCommand.Execute(null);
        vm.VisiblePages.Should().BeEmpty();
    }

    [Fact]
    public async Task VisiblePage_RendersViaDocument_OnEnsureRendered()
    {
        var vm = CreateVm();
        vm.SetTwoPageViewCommand.Execute(null);

        await vm.VisiblePages[0].EnsureRenderedAsync(default);

        vm.VisiblePages[0].Render.Should().NotBeNull();
    }

    [Fact]
    public async Task ZoomChange_RebuildsVisiblePages_DroppingStaleRenders()
    {
        var vm = CreateVm();
        vm.SetContinuousViewCommand.Execute(null);
        await vm.VisiblePages[0].EnsureRenderedAsync(default);
        vm.VisiblePages[0].Render.Should().NotBeNull();

        vm.ZoomInCommand.Execute(null);

        // Rebuilt placeholders re-render lazily at the new zoom.
        vm.VisiblePages.Should().OnlyContain(p => p.Render == null);
    }
}
