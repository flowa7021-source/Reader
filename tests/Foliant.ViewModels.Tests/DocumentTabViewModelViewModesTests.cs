using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelViewModesTests
{
    private static DocumentTabViewModel CreateVm(int pageCount = 10, PageSize? pageSize = null)
    {
        var document = Substitute.For<IDocument>();
        document.PageCount.Returns(pageCount);
        document.Metadata.Returns(new DocumentMetadata(null, null, null, null, null, new Dictionary<string, string>()));
        if (pageSize is { } ps)
        {
            document.GetPageSize(Arg.Any<int>()).Returns(ps);
        }

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
    public void Defaults_AreSinglePageAndActualSize()
    {
        var vm = CreateVm();
        vm.ViewMode.Should().Be(ViewMode.SinglePage);
        vm.FitMode.Should().Be(FitMode.ActualSize);
        vm.VisiblePageIndices.Should().Equal([0]);
    }

    [Fact]
    public void TwoPage_ShowsCurrentAndNext_ButOnlyCurrentOnLastPage()
    {
        var vm = CreateVm(pageCount: 4);
        vm.SetTwoPageViewCommand.Execute(null);

        vm.CurrentPageIndex = 1;
        vm.VisiblePageIndices.Should().Equal([1, 2]);

        vm.CurrentPageIndex = 3; // last page
        vm.VisiblePageIndices.Should().Equal([3]);
    }

    [Fact]
    public void Continuous_ShowsAllPages()
    {
        var vm = CreateVm(pageCount: 3);
        vm.SetContinuousViewCommand.Execute(null);
        vm.VisiblePageIndices.Should().Equal([0, 1, 2]);
    }

    [Fact]
    public void ModeFlags_TrackViewMode_MutuallyExclusive()
    {
        var vm = CreateVm(pageCount: 3);
        (vm.IsSinglePageView, vm.IsContinuousView, vm.IsTwoPageView).Should().Be((true, false, false));

        vm.SetContinuousViewCommand.Execute(null);
        (vm.IsSinglePageView, vm.IsContinuousView, vm.IsTwoPageView).Should().Be((false, true, false));

        vm.SetTwoPageViewCommand.Execute(null);
        (vm.IsSinglePageView, vm.IsContinuousView, vm.IsTwoPageView).Should().Be((false, false, true));
    }

    [Fact]
    public void FitWidth_ComputesZoomFromViewportAndPageSize()
    {
        var vm = CreateVm(pageSize: new PageSize(612, 792));
        vm.FitWidthCommand.Execute(null);
        vm.SetViewport(1224, 5000); // width fit → zoom 1.5

        vm.FitMode.Should().Be(FitMode.FitWidth);
        vm.Zoom.Should().BeApproximately(1.5, 0.01);
    }

    [Fact]
    public void ManualZoom_ResetsFitModeToActualSize()
    {
        var vm = CreateVm(pageSize: new PageSize(612, 792));
        vm.FitWidthCommand.Execute(null);
        vm.SetViewport(1224, 5000);
        vm.FitMode.Should().Be(FitMode.FitWidth);

        vm.ZoomInCommand.Execute(null);

        vm.FitMode.Should().Be(FitMode.ActualSize);
    }

    [Fact]
    public void ActualSize_ResetsZoomToOneHundredPercent()
    {
        var vm = CreateVm(pageSize: new PageSize(612, 792));
        vm.FitWidthCommand.Execute(null);
        vm.SetViewport(1224, 5000);

        vm.ActualSizeCommand.Execute(null);

        vm.FitMode.Should().Be(FitMode.ActualSize);
        vm.Zoom.Should().Be(1.0);
    }
}
