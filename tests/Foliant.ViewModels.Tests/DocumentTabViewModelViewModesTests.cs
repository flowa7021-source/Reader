using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Application.Settings;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelViewModesTests
{
    private static DocumentTabViewModel CreateVm(
        int pageCount = 10,
        PageSize? pageSize = null,
        ISettingsService? settings = null)
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

        return new DocumentTabViewModel(
            document, "/tmp/x.pdf", search, annotations, bookmarks,
            NullLogger<DocumentTabViewModel>.Instance,
            settings: settings);
    }

    private static ISettingsService SettingsWith(AppSettings snapshot)
    {
        var s = Substitute.For<ISettingsService>();
        s.Current.Returns(snapshot);
        return s;
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
    public void TwoPage_UsesFixedSpreads_AlignedFromZero()
    {
        var vm = CreateVm(pageCount: 4);
        vm.SetTwoPageViewCommand.Execute(null);

        // Развороты выровнены по парам (0,1),(2,3) — не «текущая+следующая».
        vm.CurrentPageIndex = 1;
        vm.VisiblePageIndices.Should().Equal([0, 1]);

        vm.CurrentPageIndex = 2;
        vm.VisiblePageIndices.Should().Equal([2, 3]);
    }

    [Fact]
    public void TwoPage_LastUnpairedPage_ShownAlone()
    {
        var vm = CreateVm(pageCount: 3); // 0,1,2 → пары (0,1) и одинокая 2
        vm.SetTwoPageViewCommand.Execute(null);

        vm.CurrentPageIndex = 2;
        vm.VisiblePageIndices.Should().Equal([2]);
    }

    [Fact]
    public void TwoPage_CoverSeparate_ShowsFirstPageAloneThenPairsFromOne()
    {
        var vm = CreateVm(pageCount: 5);
        vm.SetTwoPageViewCommand.Execute(null);
        vm.ToggleTwoPageCoverSeparateCommand.Execute(null);

        vm.CurrentPageIndex = 0;
        vm.VisiblePageIndices.Should().Equal([0]); // обложка одна

        vm.CurrentPageIndex = 1;
        vm.VisiblePageIndices.Should().Equal([1, 2]);

        vm.CurrentPageIndex = 2;
        vm.VisiblePageIndices.Should().Equal([1, 2]); // тот же разворот

        vm.CurrentPageIndex = 4; // 0 alone, (1,2),(3,4) → последняя пара полная
        vm.VisiblePageIndices.Should().Equal([3, 4]);
    }

    [Fact]
    public void ToggleCoverSeparate_RebuildsVisiblePages()
    {
        var vm = CreateVm(pageCount: 5);
        vm.SetTwoPageViewCommand.Execute(null);
        vm.VisiblePages.Select(p => p.PageIndex).Should().Equal([0, 1]);

        vm.ToggleTwoPageCoverSeparateCommand.Execute(null);
        vm.VisiblePages.Select(p => p.PageIndex).Should().Equal([0]);
    }

    [Fact]
    public void TwoPage_NextPrevPage_StepByWholeSpread()
    {
        var vm = CreateVm(pageCount: 6);
        vm.SetTwoPageViewCommand.Execute(null); // (0,1)(2,3)(4,5)

        vm.NextPageCommand.Execute(null);
        vm.VisiblePageIndices.Should().Equal([2, 3]);

        vm.NextPageCommand.Execute(null);
        vm.VisiblePageIndices.Should().Equal([4, 5]);

        vm.NextPageCommand.Execute(null); // already last spread → no move
        vm.VisiblePageIndices.Should().Equal([4, 5]);

        vm.PreviousPageCommand.Execute(null);
        vm.VisiblePageIndices.Should().Equal([2, 3]);
    }

    [Fact]
    public void TwoPage_CoverSeparate_NextPrev_RespectAloneCover()
    {
        var vm = CreateVm(pageCount: 6);
        vm.SetTwoPageViewCommand.Execute(null);
        vm.ToggleTwoPageCoverSeparateCommand.Execute(null); // 0 | (1,2)(3,4)(5)

        vm.VisiblePageIndices.Should().Equal([0]);

        vm.NextPageCommand.Execute(null);
        vm.VisiblePageIndices.Should().Equal([1, 2]);

        vm.NextPageCommand.Execute(null);
        vm.VisiblePageIndices.Should().Equal([3, 4]);

        vm.PreviousPageCommand.Execute(null);
        vm.VisiblePageIndices.Should().Equal([1, 2]);

        vm.PreviousPageCommand.Execute(null);
        vm.VisiblePageIndices.Should().Equal([0]); // back to lone cover
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

    // ───── RTL (Q-F3) ─────

    [Fact]
    public void Default_RightToLeft_IsFalse()
    {
        var vm = CreateVm();

        vm.IsRightToLeft.Should().BeFalse();
    }

    [Fact]
    public void Constructor_SnapshotsRightToLeftFromSettings()
    {
        var settings = SettingsWith(AppSettings.Default with { RightToLeft = true });

        var vm = CreateVm(settings: settings);

        vm.IsRightToLeft.Should().BeTrue();
    }

    [Fact]
    public void TwoPage_RightToLeft_SwapsPairOrder()
    {
        var vm = CreateVm(pageCount: 4);
        vm.SetTwoPageViewCommand.Execute(null);
        vm.IsRightToLeft = true;

        vm.CurrentPageIndex = 1;
        vm.VisiblePageIndices.Should().Equal([1, 0]);

        vm.CurrentPageIndex = 2;
        vm.VisiblePageIndices.Should().Equal([3, 2]);
    }

    [Fact]
    public void TwoPage_RightToLeft_LonePageStaysAlone()
    {
        var vm = CreateVm(pageCount: 3); // 0,1,2 → пара (0,1) + одинокая 2
        vm.SetTwoPageViewCommand.Execute(null);
        vm.IsRightToLeft = true;

        vm.CurrentPageIndex = 2;
        vm.VisiblePageIndices.Should().Equal([2]); // одинокая страница не переставляется
    }

    [Fact]
    public void TwoPage_RightToLeft_CoverSeparate_LoneCoverStaysAlone()
    {
        var vm = CreateVm(pageCount: 5);
        vm.SetTwoPageViewCommand.Execute(null);
        vm.ToggleTwoPageCoverSeparateCommand.Execute(null);
        vm.IsRightToLeft = true;

        vm.CurrentPageIndex = 0;
        vm.VisiblePageIndices.Should().Equal([0]); // обложка одна (RTL не меняет)

        vm.CurrentPageIndex = 1;
        vm.VisiblePageIndices.Should().Equal([2, 1]); // пара перевёрнута
    }

    [Fact]
    public void ToggleRightToLeft_FlipsFlag()
    {
        var vm = CreateVm();
        vm.IsRightToLeft.Should().BeFalse();

        vm.ToggleRightToLeftCommand.Execute(null);

        vm.IsRightToLeft.Should().BeTrue();

        vm.ToggleRightToLeftCommand.Execute(null);

        vm.IsRightToLeft.Should().BeFalse();
    }

    [Fact]
    public void ToggleRightToLeft_InTwoPageMode_RebuildsVisiblePagesInSwappedOrder()
    {
        var vm = CreateVm(pageCount: 4);
        vm.SetTwoPageViewCommand.Execute(null);
        vm.VisiblePages.Select(p => p.PageIndex).Should().Equal([0, 1]);

        vm.ToggleRightToLeftCommand.Execute(null);

        vm.VisiblePages.Select(p => p.PageIndex).Should().Equal([1, 0]);
    }

    [Fact]
    public void Continuous_RightToLeft_DoesNotAffectPageOrder()
    {
        var vm = CreateVm(pageCount: 3);
        vm.SetContinuousViewCommand.Execute(null);
        vm.IsRightToLeft = true;

        vm.VisiblePageIndices.Should().Equal([0, 1, 2]);
    }

    [Fact]
    public void SinglePage_RightToLeft_DoesNotAffectPageIndex()
    {
        var vm = CreateVm(pageCount: 3);
        vm.IsRightToLeft = true;
        vm.CurrentPageIndex = 1;

        vm.VisiblePageIndices.Should().Equal([1]);
    }
}
