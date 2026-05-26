using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

/// <summary>
/// Подсветка поиска в multi-page раскладках (Continuous/Two-Page): каждая видимая страница
/// несёт собственные <see cref="RenderedPageViewModel.SearchHighlights"/>, считаемые лениво
/// при реализации слота по активному запросу.
/// </summary>
public sealed class DocumentTabViewModelMultiPageSearchHighlightTests
{
    private static DocumentTabViewModel CreateVm(IReadOnlyDictionary<int, string[]> pageLines, int pageCount = 5)
    {
        var document = Substitute.For<IDocument>();
        document.PageCount.Returns(pageCount);
        document.Metadata.Returns(new DocumentMetadata(null, null, null, null, null, new Dictionary<string, string>()));
        document.GetPageSize(Arg.Any<int>()).Returns(new PageSize(612, 792));
        document.RenderPageAsync(Arg.Any<int>(), Arg.Any<RenderOptions>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult<IPageRender>(new FakePageRender()));
        document.GetTextLayerAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(ci =>
                {
                    int page = ci.ArgAt<int>(0);
                    return Task.FromResult<TextLayer?>(
                        pageLines.TryGetValue(page, out string[]? lines) ? LayerWith(page, lines) : null);
                });

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

    private static TextLayer LayerWith(int pageIndex, params string[] lines)
    {
        var runs = new List<TextRun>();
        for (int i = 0; i < lines.Length; i++)
        {
            runs.Add(new TextRun(lines[i], X: 10, Y: 100 - (i * 12), W: 200, H: 10));
        }
        return new TextLayer(pageIndex, runs);
    }

    [Fact]
    public async Task Continuous_PopulatesPerPageSearchHighlights_OnRealize()
    {
        var vm = CreateVm(new Dictionary<int, string[]>
        {
            [0] = ["the cat sat"],
            [1] = ["no match here"],
            [2] = ["a CAT returns"],
        }, pageCount: 3);
        vm.SetContinuousViewCommand.Execute(null);

        vm.SearchText = "cat";
        await vm.RunSearchCommand.ExecuteAsync(null);

        foreach (RenderedPageViewModel page in vm.VisiblePages)
        {
            await page.EnsureRenderedAsync(default);
        }

        vm.VisiblePages[0].SearchHighlights.Should().ContainSingle();
        vm.VisiblePages[1].SearchHighlights.Should().BeEmpty();
        vm.VisiblePages[2].SearchHighlights.Should().ContainSingle(); // case-insensitive
    }

    [Fact]
    public async Task TwoPage_PopulatesHighlights_ForCurrentAndNext()
    {
        var vm = CreateVm(new Dictionary<int, string[]>
        {
            [0] = ["the cat sat"],
            [1] = ["another cat"],
        });
        vm.SetTwoPageViewCommand.Execute(null);

        vm.SearchText = "cat";
        await vm.RunSearchCommand.ExecuteAsync(null);
        foreach (RenderedPageViewModel page in vm.VisiblePages)
        {
            await page.EnsureRenderedAsync(default);
        }

        vm.VisiblePages.Select(p => p.PageIndex).Should().Equal([0, 1]);
        vm.VisiblePages[0].SearchHighlights.Should().ContainSingle();
        vm.VisiblePages[1].SearchHighlights.Should().ContainSingle();
    }

    [Fact]
    public async Task ClearingSearch_EmptiesVisiblePageHighlights()
    {
        var vm = CreateVm(new Dictionary<int, string[]> { [0] = ["the cat sat"] }, pageCount: 2);
        vm.SetContinuousViewCommand.Execute(null);
        vm.SearchText = "cat";
        await vm.RunSearchCommand.ExecuteAsync(null);
        await vm.VisiblePages[0].EnsureRenderedAsync(default);
        vm.VisiblePages[0].SearchHighlights.Should().ContainSingle();

        vm.SearchText = "   ";
        await vm.RunSearchCommand.ExecuteAsync(null);
        await vm.VisiblePages[0].RefreshHighlightsAsync(default);

        vm.VisiblePages[0].SearchHighlights.Should().BeEmpty();
    }

    [Fact]
    public async Task NoActiveQuery_RealizingPage_LeavesHighlightsEmpty()
    {
        var vm = CreateVm(new Dictionary<int, string[]> { [0] = ["the cat sat"] }, pageCount: 2);
        vm.SetContinuousViewCommand.Execute(null);

        await vm.VisiblePages[0].EnsureRenderedAsync(default);

        vm.VisiblePages[0].SearchHighlights.Should().BeEmpty();
    }
}
