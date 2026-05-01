using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelTests
{
    private static DocumentTabViewModel CreateVm(
        IDocument? document = null,
        ISearchService? search = null,
        IAnnotationService? annotations = null,
        IBookmarkService? bookmarks = null,
        string filePath = "/tmp/x.pdf")
    {
        // Стартовая настройка применяется ТОЛЬКО когда CreateVm сам создаёт mock —
        // если caller передал свой настроенный Substitute, его .Returns(...) сетапы
        // не должны быть перетёрты дефолтами хелпера.
        if (document is null)
        {
            document = Substitute.For<IDocument>();
            document.PageCount.Returns(10);
        }

        if (search is null)
        {
            search = Substitute.For<ISearchService>();
            search
                .SearchInDocumentAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<SearchHit>>([]));
        }

        if (annotations is null)
        {
            annotations = Substitute.For<IAnnotationService>();
            annotations.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                       .Returns(Task.FromResult<IReadOnlyList<Annotation>>([]));
        }

        if (bookmarks is null)
        {
            bookmarks = Substitute.For<IBookmarkService>();
            bookmarks.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                     .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([]));
        }

        return new DocumentTabViewModel(document, filePath, search, annotations, bookmarks, NullLogger<DocumentTabViewModel>.Instance);
    }

    [Fact]
    public void Title_IsFileNameOfPath()
    {
        var vm = CreateVm(filePath: "/tmp/document.pdf");

        vm.Title.Should().Be("document.pdf");
    }

    [Fact]
    public void ToggleSearchCommand_FlipsIsSearchVisible()
    {
        var vm = CreateVm();
        vm.IsSearchVisible.Should().BeFalse();

        vm.ToggleSearchCommand.Execute(null);
        vm.IsSearchVisible.Should().BeTrue();

        vm.ToggleSearchCommand.Execute(null);
        vm.IsSearchVisible.Should().BeFalse();
    }

    [Fact]
    public async Task RunSearchCommand_EmptyText_ClearsResults_DoesNotCallService()
    {
        var search = Substitute.For<ISearchService>();
        var vm = CreateVm(search: search);
        vm.SearchResults.Add(new SearchHit("", "/x.pdf", 0, "stale", 1.0));
        vm.SearchText = "";

        await vm.RunSearchCommand.ExecuteAsync(null);

        vm.SearchResults.Should().BeEmpty();
        await search.DidNotReceive().SearchInDocumentAsync(
            Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunSearchCommand_PopulatesResultsFromService()
    {
        var hits = new SearchHit[]
        {
            new("", "/x.pdf", 1, "first hit", 1.0),
            new("", "/x.pdf", 4, "second hit", 1.0),
        };
        var search = Substitute.For<ISearchService>();
        search
            .SearchInDocumentAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SearchHit>>(hits));
        var vm = CreateVm(search: search);
        vm.SearchText = "hit";

        await vm.RunSearchCommand.ExecuteAsync(null);

        vm.SearchResults.Should().HaveCount(2);
        vm.SearchResults[0].PageIndex.Should().Be(1);
        vm.SearchResults[1].PageIndex.Should().Be(4);
    }

    [Fact]
    public void SelectingSearchHit_UpdatesCurrentPageIndex()
    {
        var vm = CreateVm();
        var hit = new SearchHit("", "/x.pdf", 7, "snippet", 1.0);

        vm.SelectedSearchHit = hit;

        vm.CurrentPageIndex.Should().Be(7);
    }

    [Fact]
    public async Task LoadAnnotations_PopulatesCurrentPageCollection()
    {
        var page0 = Annotation.Highlight(0, new AnnotationRect(0, 0, 10, 10), "#000", DateTimeOffset.UtcNow);
        var page1 = Annotation.Highlight(1, new AnnotationRect(0, 0, 10, 10), "#000", DateTimeOffset.UtcNow);
        var page2 = Annotation.Highlight(2, new AnnotationRect(0, 0, 10, 10), "#000", DateTimeOffset.UtcNow);
        var ann = Substitute.For<IAnnotationService>();
        ann.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Annotation>>([page0, page1, page2]));
        var vm = CreateVm(annotations: ann);

        await vm.LoadAnnotationsAsync(default);

        vm.CurrentPageAnnotations.Should().ContainSingle().Which.PageIndex.Should().Be(0);
    }

    [Fact]
    public async Task ChangingCurrentPage_RefiltersAnnotations()
    {
        var page0 = Annotation.Highlight(0, new AnnotationRect(0, 0, 10, 10), "#000", DateTimeOffset.UtcNow);
        var page2 = Annotation.Highlight(2, new AnnotationRect(0, 0, 10, 10), "#000", DateTimeOffset.UtcNow);
        var ann = Substitute.For<IAnnotationService>();
        ann.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Annotation>>([page0, page2]));
        var vm = CreateVm(annotations: ann);
        await vm.LoadAnnotationsAsync(default);

        vm.CurrentPageIndex = 2;

        vm.CurrentPageAnnotations.Should().ContainSingle().Which.Id.Should().Be(page2.Id);
    }

    [Fact]
    public async Task AddHighlight_DelegatesToService_AppendsIfCurrentPage()
    {
        var ann = Substitute.For<IAnnotationService>();
        ann.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Annotation>>([]));
        var vm = CreateVm(annotations: ann);
        await vm.LoadAnnotationsAsync(default);

        await vm.AddHighlightAsync(0, new AnnotationRect(1, 2, 3, 4), "#FF0", default);

        await ann.Received(1).AddAsync(
            Arg.Any<string>(),
            Arg.Is<Annotation>(a => a.Kind == AnnotationKind.Highlight && a.PageIndex == 0),
            Arg.Any<CancellationToken>());
        vm.CurrentPageAnnotations.Should().ContainSingle();
    }

    [Fact]
    public async Task AddHighlight_OnDifferentPage_DoesNotAppendToCurrent()
    {
        var ann = Substitute.For<IAnnotationService>();
        ann.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Annotation>>([]));
        var vm = CreateVm(annotations: ann);
        await vm.LoadAnnotationsAsync(default);

        await vm.AddHighlightAsync(5, new AnnotationRect(0, 0, 10, 10), "#FF0", default);

        vm.CurrentPageAnnotations.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveAnnotationCommand_DropsFromCollection_WhenServiceConfirms()
    {
        var hl = Annotation.Highlight(0, new AnnotationRect(0, 0, 10, 10), "#000", DateTimeOffset.UtcNow);
        var ann = Substitute.For<IAnnotationService>();
        ann.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Annotation>>([hl]));
        ann.RemoveAsync(Arg.Any<string>(), hl.Id, Arg.Any<CancellationToken>())
           .Returns(true);
        var vm = CreateVm(annotations: ann);
        await vm.LoadAnnotationsAsync(default);

        await vm.RemoveAnnotationCommand.ExecuteAsync(hl);

        vm.CurrentPageAnnotations.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveAnnotationCommand_ServiceReturnsFalse_LeavesCollection()
    {
        var hl = Annotation.Highlight(0, new AnnotationRect(0, 0, 10, 10), "#000", DateTimeOffset.UtcNow);
        var ann = Substitute.For<IAnnotationService>();
        ann.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Annotation>>([hl]));
        ann.RemoveAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
           .Returns(false);
        var vm = CreateVm(annotations: ann);
        await vm.LoadAnnotationsAsync(default);

        await vm.RemoveAnnotationCommand.ExecuteAsync(hl);

        vm.CurrentPageAnnotations.Should().ContainSingle();
    }

    [Fact]
    public async Task LoadAnnotations_ServiceThrows_DoesNotPropagate()
    {
        var ann = Substitute.For<IAnnotationService>();
        ann.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException<IReadOnlyList<Annotation>>(new IOException("boom")));
        var vm = CreateVm(annotations: ann);

        var act = async () => await vm.LoadAnnotationsAsync(default);

        await act.Should().NotThrowAsync();
        vm.CurrentPageAnnotations.Should().BeEmpty();
    }

    [Fact]
    public void NextPageCommand_AdvancesByOne()
    {
        var vm = CreateVm();
        vm.CurrentPageIndex = 3;

        vm.NextPageCommand.Execute(null);

        vm.CurrentPageIndex.Should().Be(4);
    }

    [Fact]
    public void NextPageCommand_AtLastPage_StaysOnLastPage()
    {
        var vm = CreateVm();
        vm.CurrentPageIndex = 9;   // PageCount=10, last=9

        vm.NextPageCommand.Execute(null);

        vm.CurrentPageIndex.Should().Be(9);
    }

    [Fact]
    public void PreviousPageCommand_DecrementsByOne()
    {
        var vm = CreateVm();
        vm.CurrentPageIndex = 5;

        vm.PreviousPageCommand.Execute(null);

        vm.CurrentPageIndex.Should().Be(4);
    }

    [Fact]
    public void PreviousPageCommand_AtFirstPage_StaysOnZero()
    {
        var vm = CreateVm();

        vm.PreviousPageCommand.Execute(null);

        vm.CurrentPageIndex.Should().Be(0);
    }

    [Fact]
    public void FirstPageCommand_GoesToZero()
    {
        var vm = CreateVm();
        vm.CurrentPageIndex = 7;

        vm.FirstPageCommand.Execute(null);

        vm.CurrentPageIndex.Should().Be(0);
    }

    [Fact]
    public void LastPageCommand_GoesToLastIndex()
    {
        var vm = CreateVm();

        vm.LastPageCommand.Execute(null);

        vm.CurrentPageIndex.Should().Be(9);   // PageCount=10
    }

    [Fact]
    public void GoToPageCommand_TranslatesOneBasedToZeroBased()
    {
        var vm = CreateVm();

        vm.GoToPageCommand.Execute(7);   // user-facing page 7

        vm.CurrentPageIndex.Should().Be(6);
    }

    [Fact]
    public void GoToPageCommand_OutOfRange_Clamps()
    {
        var vm = CreateVm();

        vm.GoToPageCommand.Execute(999);
        vm.CurrentPageIndex.Should().Be(9);

        vm.GoToPageCommand.Execute(0);
        vm.CurrentPageIndex.Should().Be(0);

        vm.GoToPageCommand.Execute(-5);
        vm.CurrentPageIndex.Should().Be(0);
    }

    [Fact]
    public void ZoomInCommand_AddsStepUpToMax()
    {
        var vm = CreateVm();
        vm.Zoom = 1.0;

        vm.ZoomInCommand.Execute(null);

        vm.Zoom.Should().BeApproximately(1.25, 1e-9);
    }

    [Fact]
    public void ZoomInCommand_NearMax_ClampsAtMax()
    {
        var vm = CreateVm();
        vm.Zoom = DocumentTabViewModel.MaxZoom - 0.10;

        vm.ZoomInCommand.Execute(null);

        vm.Zoom.Should().Be(DocumentTabViewModel.MaxZoom);
    }

    [Fact]
    public void ZoomOutCommand_SubtractsStepDownToMin()
    {
        var vm = CreateVm();
        vm.Zoom = 1.5;

        vm.ZoomOutCommand.Execute(null);

        vm.Zoom.Should().BeApproximately(1.25, 1e-9);
    }

    [Fact]
    public void ZoomOutCommand_NearMin_ClampsAtMin()
    {
        var vm = CreateVm();
        vm.Zoom = DocumentTabViewModel.MinZoom + 0.05;

        vm.ZoomOutCommand.Execute(null);

        vm.Zoom.Should().Be(DocumentTabViewModel.MinZoom);
    }

    [Fact]
    public void ResetZoomCommand_SetsToOne()
    {
        var vm = CreateVm();
        vm.Zoom = 2.5;

        vm.ResetZoomCommand.Execute(null);

        vm.Zoom.Should().Be(1.0);
    }

    [Fact]
    public void ZoomSetter_OutOfRange_Clamps()
    {
        var vm = CreateVm();

        vm.Zoom = 100.0;

        vm.Zoom.Should().Be(DocumentTabViewModel.MaxZoom);
    }

    [Fact]
    public void ZoomSetter_BelowMin_Clamps()
    {
        var vm = CreateVm();

        vm.Zoom = 0.001;

        vm.Zoom.Should().Be(DocumentTabViewModel.MinZoom);
    }

    [Fact]
    public void PageInfo_FormatsAsOneBasedSlashTotal()
    {
        var vm = CreateVm();   // PageCount = 10

        vm.PageInfo.Should().Be("1/10");

        vm.CurrentPageIndex = 4;

        vm.PageInfo.Should().Be("5/10");
    }

    [Fact]
    public void PageInfo_FiresPropertyChanged_WhenCurrentPageIndexChanges()
    {
        var vm = CreateVm();
        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        vm.CurrentPageIndex = 3;

        fired.Should().Contain(nameof(DocumentTabViewModel.PageInfo));
    }

    [Fact]
    public void ZoomPercent_RoundsZoomToInteger()
    {
        var vm = CreateVm();

        vm.Zoom = 1.0;
        vm.ZoomPercent.Should().Be(100);

        vm.Zoom = 1.25;
        vm.ZoomPercent.Should().Be(125);

        vm.Zoom = 0.50;
        vm.ZoomPercent.Should().Be(50);
    }

    [Fact]
    public void ZoomPercent_FiresPropertyChanged_WhenZoomChanges()
    {
        var vm = CreateVm();
        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        vm.Zoom = 2.0;

        fired.Should().Contain(nameof(DocumentTabViewModel.ZoomPercent));
    }

    [Fact]
    public async Task LoadBookmarks_PopulatesSortedByPageIndex()
    {
        var b3 = Bookmark.Create(3, "p3", DateTimeOffset.UtcNow);
        var b1 = Bookmark.Create(1, "p1", DateTimeOffset.UtcNow);
        var b7 = Bookmark.Create(7, "p7", DateTimeOffset.UtcNow);
        var bookmarks = Substitute.For<IBookmarkService>();
        bookmarks.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([b3, b1, b7]));
        var vm = CreateVm(bookmarks: bookmarks);

        await vm.LoadBookmarksAsync(default);

        vm.Bookmarks.Select(b => b.PageIndex).Should().Equal([1, 3, 7]);
    }

    [Fact]
    public async Task ToggleBookmark_NoExisting_AddsBookmark_InsertsByPage()
    {
        var existingP1 = Bookmark.Create(1, "p1", DateTimeOffset.UtcNow);
        var bookmarks = Substitute.For<IBookmarkService>();
        bookmarks.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([existingP1]));
        bookmarks.ToggleAsync(Arg.Any<string>(), 5, Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<Bookmark?>(Bookmark.Create(5, "Page 6", DateTimeOffset.UtcNow)));
        var vm = CreateVm(bookmarks: bookmarks);
        await vm.LoadBookmarksAsync(default);
        vm.CurrentPageIndex = 5;

        await vm.ToggleBookmarkCommand.ExecuteAsync(null);

        vm.Bookmarks.Select(b => b.PageIndex).Should().Equal([1, 5]);
    }

    [Fact]
    public async Task ToggleBookmark_ExistingOnPage_DropsIt()
    {
        var existing = Bookmark.Create(2, "p2", DateTimeOffset.UtcNow);
        var bookmarks = Substitute.For<IBookmarkService>();
        bookmarks.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([existing]));
        bookmarks.ToggleAsync(Arg.Any<string>(), 2, Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<Bookmark?>(null));
        var vm = CreateVm(bookmarks: bookmarks);
        await vm.LoadBookmarksAsync(default);
        vm.CurrentPageIndex = 2;

        await vm.ToggleBookmarkCommand.ExecuteAsync(null);

        vm.Bookmarks.Should().BeEmpty();
    }

    [Fact]
    public void JumpToBookmark_SetsCurrentPageIndex()
    {
        var vm = CreateVm();
        var bm = Bookmark.Create(7, "ch3", DateTimeOffset.UtcNow);

        vm.JumpToBookmarkCommand.Execute(bm);

        vm.CurrentPageIndex.Should().Be(7);
    }

    [Fact]
    public void JumpToBookmark_Null_NoOp()
    {
        var vm = CreateVm();
        vm.CurrentPageIndex = 3;

        vm.JumpToBookmarkCommand.Execute(null);

        vm.CurrentPageIndex.Should().Be(3);
    }

    [Fact]
    public async Task LoadBookmarks_ServiceThrows_DoesNotPropagate()
    {
        var bookmarks = Substitute.For<IBookmarkService>();
        bookmarks.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromException<IReadOnlyList<Bookmark>>(new IOException("boom")));
        var vm = CreateVm(bookmarks: bookmarks);

        var act = async () => await vm.LoadBookmarksAsync(default);

        await act.Should().NotThrowAsync();
        vm.Bookmarks.Should().BeEmpty();
    }

    // ───── Recent searches (S11/F) ─────

    [Fact]
    public async Task RunSearch_PromotesQueryToRecent_MostRecentFirst()
    {
        var search = Substitute.For<ISearchService>();
        search.SearchInDocumentAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<IReadOnlyList<SearchHit>>([]));
        var vm = CreateVm(search: search);

        vm.SearchText = "first";
        await vm.RunSearchCommand.ExecuteAsync(null);

        vm.SearchText = "second";
        await vm.RunSearchCommand.ExecuteAsync(null);

        vm.RecentSearches.Should().Equal(["second", "first"]);
    }

    [Fact]
    public async Task RunSearch_DedupsCaseInsensitively_PromotesExisting()
    {
        var search = Substitute.For<ISearchService>();
        search.SearchInDocumentAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<IReadOnlyList<SearchHit>>([]));
        var vm = CreateVm(search: search);

        vm.SearchText = "Foliant";
        await vm.RunSearchCommand.ExecuteAsync(null);
        vm.SearchText = "anything";
        await vm.RunSearchCommand.ExecuteAsync(null);
        vm.SearchText = "FOLIANT";   // тот же запрос с другим кейсом
        await vm.RunSearchCommand.ExecuteAsync(null);

        vm.RecentSearches.Should().Equal(["FOLIANT", "anything"]);
    }

    [Fact]
    public async Task RunSearch_RecentCappedAtMax()
    {
        var search = Substitute.For<ISearchService>();
        search.SearchInDocumentAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<IReadOnlyList<SearchHit>>([]));
        var vm = CreateVm(search: search);

        for (int i = 0; i < DocumentTabViewModel.MaxRecentSearches + 5; i++)
        {
            vm.SearchText = $"q{i}";
            await vm.RunSearchCommand.ExecuteAsync(null);
        }

        vm.RecentSearches.Count.Should().Be(DocumentTabViewModel.MaxRecentSearches);
        vm.RecentSearches[0].Should().Be($"q{DocumentTabViewModel.MaxRecentSearches + 4}");
    }

    [Fact]
    public async Task RunSearch_EmptyText_DoesNotPromote()
    {
        var search = Substitute.For<ISearchService>();
        search.SearchInDocumentAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<IReadOnlyList<SearchHit>>([]));
        var vm = CreateVm(search: search);
        vm.SearchText = "";

        await vm.RunSearchCommand.ExecuteAsync(null);

        vm.RecentSearches.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectRecentSearchCommand_FillsSearchTextAndRunsSearch()
    {
        var hits = new SearchHit[] { new("", "/x.pdf", 2, "snip", 1.0) };
        var search = Substitute.For<ISearchService>();
        search.SearchInDocumentAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<IReadOnlyList<SearchHit>>(hits));
        var vm = CreateVm(search: search);

        await vm.SelectRecentSearchCommand.ExecuteAsync("foo");

        vm.SearchText.Should().Be("foo");
        vm.SearchResults.Should().ContainSingle();
    }

    // ───── Find next/previous (S11/H) ─────

    [Fact]
    public void NextSearchHit_NoResults_NoOp()
    {
        var vm = CreateVm();

        var act = () => vm.NextSearchHitCommand.Execute(null);
        act.Should().NotThrow();
        vm.SelectedSearchHit.Should().BeNull();
    }

    [Fact]
    public void NextSearchHit_NoSelection_PicksFirst()
    {
        var vm = CreateVm();
        var h0 = new SearchHit("", "/x", 0, "a", 1.0);
        var h1 = new SearchHit("", "/x", 5, "b", 1.0);
        vm.SearchResults.Add(h0);
        vm.SearchResults.Add(h1);

        vm.NextSearchHitCommand.Execute(null);

        vm.SelectedSearchHit.Should().BeSameAs(h0);
    }

    [Fact]
    public void NextSearchHit_CyclesAndWraps()
    {
        var vm = CreateVm();
        var h0 = new SearchHit("", "/x", 0, "a", 1.0);
        var h1 = new SearchHit("", "/x", 1, "b", 1.0);
        var h2 = new SearchHit("", "/x", 2, "c", 1.0);
        vm.SearchResults.Add(h0);
        vm.SearchResults.Add(h1);
        vm.SearchResults.Add(h2);
        vm.SelectedSearchHit = h1;

        vm.NextSearchHitCommand.Execute(null);
        vm.SelectedSearchHit.Should().BeSameAs(h2);

        vm.NextSearchHitCommand.Execute(null);
        vm.SelectedSearchHit.Should().BeSameAs(h0);   // wrap
    }

    [Fact]
    public void PreviousSearchHit_WrapsAtStart()
    {
        var vm = CreateVm();
        var h0 = new SearchHit("", "/x", 0, "a", 1.0);
        var h1 = new SearchHit("", "/x", 1, "b", 1.0);
        vm.SearchResults.Add(h0);
        vm.SearchResults.Add(h1);
        vm.SelectedSearchHit = h0;

        vm.PreviousSearchHitCommand.Execute(null);

        vm.SelectedSearchHit.Should().BeSameAs(h1);   // wrap
    }

    // ───── Page navigation history (S11/I) ─────

    [Fact]
    public void NavigationHistory_FreshTab_CannotGoBackOrForward()
    {
        var vm = CreateVm();

        vm.CanGoBack.Should().BeFalse();
        vm.CanGoForward.Should().BeFalse();
    }

    [Fact]
    public void NavigationHistory_PageChangePushesBack_ClearsForward()
    {
        var vm = CreateVm();

        vm.CurrentPageIndex = 5;

        vm.CanGoBack.Should().BeTrue();   // от 0 ушли — есть back
        vm.CanGoForward.Should().BeFalse();
    }

    [Fact]
    public void GoBack_RestoresPreviousPage_PushesCurrentToForward()
    {
        var vm = CreateVm();
        vm.CurrentPageIndex = 5;

        vm.GoBackCommand.Execute(null);

        vm.CurrentPageIndex.Should().Be(0);
        vm.CanGoBack.Should().BeFalse();
        vm.CanGoForward.Should().BeTrue();
    }

    [Fact]
    public void GoBack_GoForward_RoundTrip()
    {
        var vm = CreateVm();
        vm.CurrentPageIndex = 3;
        vm.CurrentPageIndex = 7;

        vm.GoBackCommand.Execute(null);
        vm.CurrentPageIndex.Should().Be(3);

        vm.GoForwardCommand.Execute(null);
        vm.CurrentPageIndex.Should().Be(7);
    }

    [Fact]
    public void NavigationHistory_NewJumpAfterBack_ClearsForwardStack()
    {
        var vm = CreateVm();
        vm.CurrentPageIndex = 3;
        vm.CurrentPageIndex = 7;
        vm.GoBackCommand.Execute(null);   // на 3, forward = [7]
        vm.CanGoForward.Should().BeTrue();

        vm.CurrentPageIndex = 5;          // новая навигация — forward сбрасывается

        vm.CanGoForward.Should().BeFalse();
    }

    [Fact]
    public void GoBackCommand_EmptyStack_NoOp()
    {
        var vm = CreateVm();

        var act = () => vm.GoBackCommand.Execute(null);
        act.Should().NotThrow();
        vm.CurrentPageIndex.Should().Be(0);
    }

    [Fact]
    public void GoForwardCommand_EmptyStack_NoOp()
    {
        var vm = CreateVm();
        vm.CurrentPageIndex = 5;

        var act = () => vm.GoForwardCommand.Execute(null);
        act.Should().NotThrow();
        vm.CurrentPageIndex.Should().Be(5);
    }

    [Fact]
    public void ClearNavigationHistoryCommand_DropsBothStacks()
    {
        var vm = CreateVm();
        vm.CurrentPageIndex = 3;
        vm.CurrentPageIndex = 7;
        vm.GoBackCommand.Execute(null);   // back=[0], forward=[7]

        vm.CanGoBack.Should().BeTrue();
        vm.CanGoForward.Should().BeTrue();

        vm.ClearNavigationHistoryCommand.Execute(null);

        vm.CanGoBack.Should().BeFalse();
        vm.CanGoForward.Should().BeFalse();
    }

    [Fact]
    public void ClearNavigationHistoryCommand_EmptyStacks_NoOp()
    {
        var vm = CreateVm();

        var act = () => vm.ClearNavigationHistoryCommand.Execute(null);

        act.Should().NotThrow();
        vm.CanGoBack.Should().BeFalse();
        vm.CanGoForward.Should().BeFalse();
    }

    // ───── Search options on VM (S6/C) ─────

    [Fact]
    public async Task RunSearch_PassesMatchCaseAndWholeWord_To_SearchQuery()
    {
        SearchQuery? captured = null;
        var search = Substitute.For<ISearchService>();
        search.SearchInDocumentAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
              .Returns(call =>
              {
                  captured = (SearchQuery)call[2];
                  return Task.FromResult<IReadOnlyList<SearchHit>>([]);
              });
        var vm = CreateVm(search: search);
        vm.SearchMatchCase = true;
        vm.SearchMatchWholeWord = true;
        vm.SearchText = "Foo";

        await vm.RunSearchCommand.ExecuteAsync(null);

        captured.Should().NotBeNull();
        captured!.MatchCase.Should().BeTrue();
        captured.MatchWholeWord.Should().BeTrue();
        captured.Text.Should().Be("Foo");
    }

    [Fact]
    public async Task RunSearch_DefaultOptions_AreFalse()
    {
        SearchQuery? captured = null;
        var search = Substitute.For<ISearchService>();
        search.SearchInDocumentAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
              .Returns(call =>
              {
                  captured = (SearchQuery)call[2];
                  return Task.FromResult<IReadOnlyList<SearchHit>>([]);
              });
        var vm = CreateVm(search: search);
        vm.SearchText = "x";

        await vm.RunSearchCommand.ExecuteAsync(null);

        captured!.MatchCase.Should().BeFalse();
        captured.MatchWholeWord.Should().BeFalse();
    }

    // ───── Per-kind counts (S11/S) ─────

    [Fact]
    public async Task PerKindCounts_StartAtZero_GrowWithLoad()
    {
        var hl = Annotation.Highlight(0, new AnnotationRect(0, 0, 1, 1), "#000", DateTimeOffset.UtcNow);
        var note = Annotation.StickyNote(0, new AnnotationRect(0, 0, 1, 1), "n", "#000", DateTimeOffset.UtcNow);
        var ink = Annotation.Freehand(0, [new AnnotationPoint(0, 0)], "#000", DateTimeOffset.UtcNow);
        var ann = Substitute.For<IAnnotationService>();
        ann.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Annotation>>([hl, note, ink, hl]));
        var vm = CreateVm(annotations: ann);

        vm.HighlightCount.Should().Be(0);
        vm.NoteCount.Should().Be(0);
        vm.FreehandCount.Should().Be(0);

        await vm.LoadAnnotationsAsync(default);

        vm.HighlightCount.Should().Be(2);
        vm.NoteCount.Should().Be(1);
        vm.FreehandCount.Should().Be(1);
        vm.TotalAnnotationsCount.Should().Be(4);
    }

    [Fact]
    public async Task PerKindCounts_FirePropertyChanged_OnAdd()
    {
        var ann = Substitute.For<IAnnotationService>();
        ann.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Annotation>>([]));
        var vm = CreateVm(annotations: ann);
        await vm.LoadAnnotationsAsync(default);

        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        await vm.AddHighlightAsync(0, new AnnotationRect(0, 0, 1, 1), "#000", default);

        fired.Should().Contain(nameof(DocumentTabViewModel.HighlightCount));
        fired.Should().Contain(nameof(DocumentTabViewModel.TotalAnnotationsCount));
        fired.Should().Contain(nameof(DocumentTabViewModel.NoteCount));   // helper рейзит для всех — это ок
    }

    // ───── Next/Previous bookmark (S11/K) ─────

    [Fact]
    public async Task NextBookmark_WithBookmarks_JumpsToNearestAheadOfCurrent()
    {
        var b0 = Bookmark.Create(0, "p0", DateTimeOffset.UtcNow);
        var b3 = Bookmark.Create(3, "p3", DateTimeOffset.UtcNow);
        var b7 = Bookmark.Create(7, "p7", DateTimeOffset.UtcNow);
        var bookmarks = Substitute.For<IBookmarkService>();
        bookmarks.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([b0, b3, b7]));
        var vm = CreateVm(bookmarks: bookmarks);
        await vm.LoadBookmarksAsync(default);
        vm.CurrentPageIndex = 4;

        vm.NextBookmarkCommand.Execute(null);

        vm.CurrentPageIndex.Should().Be(7);
    }

    [Fact]
    public async Task NextBookmark_NoneAhead_WrapsToFirst()
    {
        var b0 = Bookmark.Create(0, "p0", DateTimeOffset.UtcNow);
        var b3 = Bookmark.Create(3, "p3", DateTimeOffset.UtcNow);
        var bookmarks = Substitute.For<IBookmarkService>();
        bookmarks.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([b0, b3]));
        var vm = CreateVm(bookmarks: bookmarks);
        await vm.LoadBookmarksAsync(default);
        vm.CurrentPageIndex = 9;

        vm.NextBookmarkCommand.Execute(null);

        vm.CurrentPageIndex.Should().Be(0);
    }

    [Fact]
    public async Task PreviousBookmark_WithBookmarks_JumpsToNearestBehindCurrent()
    {
        var b1 = Bookmark.Create(1, "p1", DateTimeOffset.UtcNow);
        var b5 = Bookmark.Create(5, "p5", DateTimeOffset.UtcNow);
        var b8 = Bookmark.Create(8, "p8", DateTimeOffset.UtcNow);
        var bookmarks = Substitute.For<IBookmarkService>();
        bookmarks.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([b1, b5, b8]));
        var vm = CreateVm(bookmarks: bookmarks);
        await vm.LoadBookmarksAsync(default);
        vm.CurrentPageIndex = 7;

        vm.PreviousBookmarkCommand.Execute(null);

        vm.CurrentPageIndex.Should().Be(5);
    }

    [Fact]
    public async Task PreviousBookmark_NoneBehind_WrapsToLast()
    {
        var b3 = Bookmark.Create(3, "p3", DateTimeOffset.UtcNow);
        var b7 = Bookmark.Create(7, "p7", DateTimeOffset.UtcNow);
        var bookmarks = Substitute.For<IBookmarkService>();
        bookmarks.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([b3, b7]));
        var vm = CreateVm(bookmarks: bookmarks);
        await vm.LoadBookmarksAsync(default);
        vm.CurrentPageIndex = 1;

        vm.PreviousBookmarkCommand.Execute(null);

        vm.CurrentPageIndex.Should().Be(7);
    }

    [Fact]
    public void NextBookmark_Empty_NoOp()
    {
        var vm = CreateVm();
        vm.CurrentPageIndex = 3;

        vm.NextBookmarkCommand.Execute(null);

        vm.CurrentPageIndex.Should().Be(3);
    }

    // ───── Counts (S11/L) ─────

    [Fact]
    public async Task TotalAnnotationsCount_StartsAtZero_GrowsWithLoadAndAdd()
    {
        var page0 = Annotation.Highlight(0, new AnnotationRect(0, 0, 10, 10), "#000", DateTimeOffset.UtcNow);
        var page2 = Annotation.Highlight(2, new AnnotationRect(0, 0, 10, 10), "#000", DateTimeOffset.UtcNow);
        var ann = Substitute.For<IAnnotationService>();
        ann.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Annotation>>([page0, page2]));
        var vm = CreateVm(annotations: ann);

        vm.TotalAnnotationsCount.Should().Be(0);

        await vm.LoadAnnotationsAsync(default);
        vm.TotalAnnotationsCount.Should().Be(2);

        await vm.AddHighlightAsync(5, new AnnotationRect(0, 0, 1, 1), "#FF0", default);
        vm.TotalAnnotationsCount.Should().Be(3);
    }

    [Fact]
    public void CurrentPageAnnotationsCount_FollowsCollection()
    {
        var vm = CreateVm();
        vm.CurrentPageAnnotationsCount.Should().Be(0);

        vm.CurrentPageAnnotations.Add(Annotation.Highlight(0, new AnnotationRect(0, 0, 1, 1), "#000", DateTimeOffset.UtcNow));
        vm.CurrentPageAnnotationsCount.Should().Be(1);

        vm.CurrentPageAnnotations.Clear();
        vm.CurrentPageAnnotationsCount.Should().Be(0);
    }

    [Fact]
    public async Task BookmarksCount_FollowsCollection()
    {
        var b0 = Bookmark.Create(0, "p0", DateTimeOffset.UtcNow);
        var b3 = Bookmark.Create(3, "p3", DateTimeOffset.UtcNow);
        var bookmarks = Substitute.For<IBookmarkService>();
        bookmarks.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([b0, b3]));
        var vm = CreateVm(bookmarks: bookmarks);

        vm.BookmarksCount.Should().Be(0);

        await vm.LoadBookmarksAsync(default);
        vm.BookmarksCount.Should().Be(2);
    }

    [Fact]
    public void Counts_FirePropertyChanged_OnMutation()
    {
        var vm = CreateVm();
        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        vm.CurrentPageAnnotations.Add(Annotation.Highlight(0, new AnnotationRect(0, 0, 1, 1), "#000", DateTimeOffset.UtcNow));
        fired.Should().Contain(nameof(DocumentTabViewModel.CurrentPageAnnotationsCount));

        fired.Clear();
        vm.Bookmarks.Add(Bookmark.Create(0, "x", DateTimeOffset.UtcNow));
        fired.Should().Contain(nameof(DocumentTabViewModel.BookmarksCount));
    }

    // ───── Metadata exposure (S11/M) ─────

    [Fact]
    public void Metadata_BuiltFromDocumentMetadata_ContainsTitleAndPageCount()
    {
        var doc = Substitute.For<IDocument>();
        doc.PageCount.Returns(42);
        doc.Metadata.Returns(new DocumentMetadata(
            Title: "Sample",
            Author: null,
            Subject: null,
            Created: null,
            Modified: null,
            Custom: new Dictionary<string, string>()));
        var vm = CreateVm(document: doc, filePath: "/tmp/sample.pdf");

        var meta = vm.Metadata;

        meta.Title.Should().Be("Sample");
        meta.PageCount.Should().Be(42);
        meta.FilePath.Should().Be("/tmp/sample.pdf");
    }

    [Fact]
    public void Metadata_LazyEvaluated_SameInstanceAcrossAccesses()
    {
        var vm = CreateVm();

        var first = vm.Metadata;
        var second = vm.Metadata;

        second.Should().BeSameAs(first);
    }

    [Fact]
    public void Metadata_NoFieldsPresent_HasAnyKnownField_False()
    {
        var doc = Substitute.For<IDocument>();
        doc.PageCount.Returns(1);
        doc.Metadata.Returns(DocumentMetadata.Empty);
        var vm = CreateVm(document: doc);

        vm.Metadata.HasAnyKnownField.Should().BeFalse();
    }

    // ───── Annotation filter mode (S10/E) ─────

    [Fact]
    public async Task AnnotationFilter_DefaultAll_ShowsEverythingOnCurrentPage()
    {
        var hl = Annotation.Highlight(0, new AnnotationRect(0, 0, 1, 1), "#000", DateTimeOffset.UtcNow);
        var note = Annotation.StickyNote(0, new AnnotationRect(0, 0, 1, 1), "n", "#000", DateTimeOffset.UtcNow);
        var ann = Substitute.For<IAnnotationService>();
        ann.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Annotation>>([hl, note]));
        var vm = CreateVm(annotations: ann);

        await vm.LoadAnnotationsAsync(default);

        vm.AnnotationFilter.Should().Be(AnnotationFilterMode.All);
        vm.CurrentPageAnnotations.Should().HaveCount(2);
    }

    [Fact]
    public async Task AnnotationFilter_HighlightsOnly_HidesOtherKinds()
    {
        var hl = Annotation.Highlight(0, new AnnotationRect(0, 0, 1, 1), "#000", DateTimeOffset.UtcNow);
        var note = Annotation.StickyNote(0, new AnnotationRect(0, 0, 1, 1), "n", "#000", DateTimeOffset.UtcNow);
        var ink = Annotation.Freehand(0, [new AnnotationPoint(0, 0)], "#000", DateTimeOffset.UtcNow);
        var ann = Substitute.For<IAnnotationService>();
        ann.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Annotation>>([hl, note, ink]));
        var vm = CreateVm(annotations: ann);
        await vm.LoadAnnotationsAsync(default);

        vm.AnnotationFilter = AnnotationFilterMode.Highlights;

        vm.CurrentPageAnnotations.Should().ContainSingle().Which.Kind.Should().Be(AnnotationKind.Highlight);
    }

    [Fact]
    public async Task AnnotationFilter_NotesOnly_FiltersToNotes()
    {
        var hl = Annotation.Highlight(0, new AnnotationRect(0, 0, 1, 1), "#000", DateTimeOffset.UtcNow);
        var note = Annotation.StickyNote(0, new AnnotationRect(0, 0, 1, 1), "n", "#000", DateTimeOffset.UtcNow);
        var ann = Substitute.For<IAnnotationService>();
        ann.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Annotation>>([hl, note]));
        var vm = CreateVm(annotations: ann);
        await vm.LoadAnnotationsAsync(default);

        vm.AnnotationFilter = AnnotationFilterMode.Notes;

        vm.CurrentPageAnnotations.Should().ContainSingle().Which.Kind.Should().Be(AnnotationKind.StickyNote);
    }

    [Fact]
    public async Task AnnotationFilter_AddingFilteredOutKind_DoesNotAppendToView()
    {
        var ann = Substitute.For<IAnnotationService>();
        ann.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Annotation>>([]));
        var vm = CreateVm(annotations: ann);
        await vm.LoadAnnotationsAsync(default);
        vm.AnnotationFilter = AnnotationFilterMode.Highlights;

        await vm.AddNoteAsync(0, new AnnotationRect(0, 0, 1, 1), "hidden", "#000", default);

        vm.CurrentPageAnnotations.Should().BeEmpty();
        vm.TotalAnnotationsCount.Should().Be(1);
    }

    [Fact]
    public async Task AnnotationFilter_ChangingBackToAll_RevealsHidden()
    {
        var hl = Annotation.Highlight(0, new AnnotationRect(0, 0, 1, 1), "#000", DateTimeOffset.UtcNow);
        var note = Annotation.StickyNote(0, new AnnotationRect(0, 0, 1, 1), "n", "#000", DateTimeOffset.UtcNow);
        var ann = Substitute.For<IAnnotationService>();
        ann.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Annotation>>([hl, note]));
        var vm = CreateVm(annotations: ann);
        await vm.LoadAnnotationsAsync(default);
        vm.AnnotationFilter = AnnotationFilterMode.Highlights;
        vm.CurrentPageAnnotations.Should().ContainSingle();

        vm.AnnotationFilter = AnnotationFilterMode.All;

        vm.CurrentPageAnnotations.Should().HaveCount(2);
    }

    // ───── Search «n of m» indicator (S11/X) ─────

    [Fact]
    public void SearchHitInfo_NoSearch_IsEmpty()
    {
        var vm = CreateVm();

        vm.SearchHitCount.Should().Be(0);
        vm.SelectedSearchHitOneBasedIndex.Should().Be(0);
        vm.SearchHitInfo.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchHitInfo_AfterRunSearch_ShowsCountWithoutSelection()
    {
        var hits = new SearchHit[]
        {
            new("", "/x.pdf", 1, "a", 1.0),
            new("", "/x.pdf", 2, "b", 1.0),
            new("", "/x.pdf", 3, "c", 1.0),
        };
        var search = Substitute.For<ISearchService>();
        search
            .SearchInDocumentAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SearchHit>>(hits));
        var vm = CreateVm(search: search);
        vm.SearchText = "foo";

        await vm.RunSearchCommand.ExecuteAsync(null);

        vm.SearchHitCount.Should().Be(3);
        // Без явного SelectedSearchHit индекс = 0 → отображение «0/3».
        vm.SelectedSearchHitOneBasedIndex.Should().Be(0);
        vm.SearchHitInfo.Should().Be("0/3");
    }

    [Fact]
    public async Task SearchHitInfo_SelectingHit_UpdatesIndex()
    {
        var hits = new SearchHit[]
        {
            new("", "/x.pdf", 1, "a", 1.0),
            new("", "/x.pdf", 2, "b", 1.0),
            new("", "/x.pdf", 3, "c", 1.0),
        };
        var search = Substitute.For<ISearchService>();
        search
            .SearchInDocumentAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SearchHit>>(hits));
        var vm = CreateVm(search: search);
        vm.SearchText = "foo";
        await vm.RunSearchCommand.ExecuteAsync(null);

        vm.SelectedSearchHit = vm.SearchResults[1];

        vm.SelectedSearchHitOneBasedIndex.Should().Be(2);
        vm.SearchHitInfo.Should().Be("2/3");
    }

    [Fact]
    public async Task SearchHitInfo_NextSearchHit_AdvancesIndex()
    {
        var hits = new SearchHit[]
        {
            new("", "/x.pdf", 1, "a", 1.0),
            new("", "/x.pdf", 2, "b", 1.0),
        };
        var search = Substitute.For<ISearchService>();
        search
            .SearchInDocumentAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SearchHit>>(hits));
        var vm = CreateVm(search: search);
        vm.SearchText = "foo";
        await vm.RunSearchCommand.ExecuteAsync(null);

        vm.NextSearchHitCommand.Execute(null);
        vm.SearchHitInfo.Should().Be("1/2");

        vm.NextSearchHitCommand.Execute(null);
        vm.SearchHitInfo.Should().Be("2/2");
    }

    [Fact]
    public async Task SearchResults_PropertyChanged_FiresForCountAndInfo()
    {
        var hits = new SearchHit[]
        {
            new("", "/x.pdf", 1, "a", 1.0),
        };
        var search = Substitute.For<ISearchService>();
        search
            .SearchInDocumentAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SearchHit>>(hits));
        var vm = CreateVm(search: search);
        vm.SearchText = "foo";

        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        await vm.RunSearchCommand.ExecuteAsync(null);

        fired.Should().Contain(nameof(DocumentTabViewModel.SearchHitCount));
        fired.Should().Contain(nameof(DocumentTabViewModel.SearchHitInfo));
    }

    // ───── UpdateAnnotationCommand (S10/J) ─────

    [Fact]
    public async Task UpdateAnnotationCommand_CallsServiceAndUpdatesAllAnnotations()
    {
        var original = Annotation.StickyNote(0, new AnnotationRect(0, 0, 10, 10), "old text", "#000", DateTimeOffset.UtcNow);
        var ann = Substitute.For<IAnnotationService>();
        ann.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Annotation>>([original]));
        ann.UpdateAsync(Arg.Any<string>(), Arg.Any<Annotation>(), Arg.Any<CancellationToken>())
           .Returns(Task.CompletedTask);
        var vm = CreateVm(annotations: ann);
        await vm.LoadAnnotationsAsync(default);

        var updated = original with { Text = "new text" };
        await vm.UpdateAnnotationCommand.ExecuteAsync(updated);

        await ann.Received(1).UpdateAsync(
            Arg.Any<string>(),
            Arg.Is<Annotation>(a => a.Id == original.Id && a.Text == "new text"),
            Arg.Any<CancellationToken>());
        vm.TotalAnnotationsCount.Should().Be(1);
    }

    [Fact]
    public async Task UpdateAnnotationCommand_UpdatesCurrentPageAnnotations_WhenOnCurrentPage()
    {
        var original = Annotation.StickyNote(0, new AnnotationRect(0, 0, 10, 10), "before", "#000", DateTimeOffset.UtcNow);
        var ann = Substitute.For<IAnnotationService>();
        ann.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Annotation>>([original]));
        ann.UpdateAsync(Arg.Any<string>(), Arg.Any<Annotation>(), Arg.Any<CancellationToken>())
           .Returns(Task.CompletedTask);
        var vm = CreateVm(annotations: ann);
        await vm.LoadAnnotationsAsync(default);

        var updated = original with { Text = "after" };
        await vm.UpdateAnnotationCommand.ExecuteAsync(updated);

        vm.CurrentPageAnnotations.Should().ContainSingle()
            .Which.Text.Should().Be("after");
    }

    [Fact]
    public async Task UpdateAnnotationCommand_AnnotationOnOtherPage_DoesNotTouchCurrentPage()
    {
        var page3 = Annotation.StickyNote(3, new AnnotationRect(0, 0, 10, 10), "p3", "#000", DateTimeOffset.UtcNow);
        var ann = Substitute.For<IAnnotationService>();
        ann.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Annotation>>([page3]));
        ann.UpdateAsync(Arg.Any<string>(), Arg.Any<Annotation>(), Arg.Any<CancellationToken>())
           .Returns(Task.CompletedTask);
        var vm = CreateVm(annotations: ann);
        await vm.LoadAnnotationsAsync(default); // CurrentPage = 0

        await vm.UpdateAnnotationCommand.ExecuteAsync(page3 with { Text = "updated" });

        vm.CurrentPageAnnotations.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateAnnotationCommand_NullArg_IsNoOp()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(annotations: ann);

        await vm.UpdateAnnotationCommand.ExecuteAsync(null);

        await ann.DidNotReceive().UpdateAsync(
            Arg.Any<string>(), Arg.Any<Annotation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAnnotationCommand_UnknownId_DoesNotCrash()
    {
        var ann = Substitute.For<IAnnotationService>();
        ann.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Annotation>>([]));
        ann.UpdateAsync(Arg.Any<string>(), Arg.Any<Annotation>(), Arg.Any<CancellationToken>())
           .Returns(Task.CompletedTask);
        var vm = CreateVm(annotations: ann);
        await vm.LoadAnnotationsAsync(default);

        var orphan = Annotation.StickyNote(0, new AnnotationRect(0, 0, 1, 1), "x", "#000", DateTimeOffset.UtcNow);
        var act = async () => await vm.UpdateAnnotationCommand.ExecuteAsync(orphan);

        await act.Should().NotThrowAsync();
    }

    // ───── RenameBookmarkCommand (S7/C) ─────

    [Fact]
    public async Task RenameBookmarkCommand_UpdatesLabelInCollection()
    {
        var original = Bookmark.Create(3, "old label", DateTimeOffset.UtcNow);
        var renamed = original with { Label = "new label" };
        var bm = Substitute.For<IBookmarkService>();
        bm.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([original]));
        bm.RenameAsync(Arg.Any<string>(), original.Id, "new label", Arg.Any<CancellationToken>())
          .Returns(Task.FromResult<Bookmark?>(renamed));
        var vm = CreateVm(bookmarks: bm);
        await vm.LoadBookmarksAsync(default);

        await vm.RenameBookmarkCommand.ExecuteAsync(new RenameBookmarkRequest(original, "new label"));

        vm.Bookmarks.Should().ContainSingle().Which.Label.Should().Be("new label");
    }

    [Fact]
    public async Task RenameBookmarkCommand_ServiceReturnsNull_LeavesCollectionUnchanged()
    {
        var original = Bookmark.Create(2, "keep", DateTimeOffset.UtcNow);
        var bm = Substitute.For<IBookmarkService>();
        bm.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([original]));
        bm.RenameAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(Task.FromResult<Bookmark?>(null));
        var vm = CreateVm(bookmarks: bm);
        await vm.LoadBookmarksAsync(default);

        await vm.RenameBookmarkCommand.ExecuteAsync(new RenameBookmarkRequest(original, "fail"));

        vm.Bookmarks.Should().ContainSingle().Which.Label.Should().Be("keep");
    }

    [Fact]
    public async Task RenameBookmarkCommand_NullRequest_IsNoOp()
    {
        var bm = Substitute.For<IBookmarkService>();
        var vm = CreateVm(bookmarks: bm);

        await vm.RenameBookmarkCommand.ExecuteAsync(null);

        await bm.DidNotReceive().RenameAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RenameBookmarkCommand_EmptyOrWhitespaceLabel_IsNoOp(string newLabel)
    {
        var bm = Substitute.For<IBookmarkService>();
        var original = Bookmark.Create(0, "x", DateTimeOffset.UtcNow);
        var vm = CreateVm(bookmarks: bm);

        await vm.RenameBookmarkCommand.ExecuteAsync(new RenameBookmarkRequest(original, newLabel));

        await bm.DidNotReceive().RenameAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RenameBookmarkCommand_TrimsWhitespace_PassesTrimmedLabel()
    {
        var original = Bookmark.Create(0, "x", DateTimeOffset.UtcNow);
        var renamed = original with { Label = "trimmed" };
        var bm = Substitute.For<IBookmarkService>();
        bm.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([original]));
        bm.RenameAsync(Arg.Any<string>(), original.Id, "trimmed", Arg.Any<CancellationToken>())
          .Returns(Task.FromResult<Bookmark?>(renamed));
        var vm = CreateVm(bookmarks: bm);
        await vm.LoadBookmarksAsync(default);

        await vm.RenameBookmarkCommand.ExecuteAsync(new RenameBookmarkRequest(original, "  trimmed  "));

        await bm.Received(1).RenameAsync(
            Arg.Any<string>(), original.Id, "trimmed", Arg.Any<CancellationToken>());
    }

    // ───── ISearchHistoryService wiring (S6/F) ─────

    private static DocumentTabViewModel CreateVmWithHistory(
        ISearchHistoryService history,
        ISearchService? search = null)
    {
        var doc = Substitute.For<IDocument>();
        doc.PageCount.Returns(10);

        if (search is null)
        {
            search = Substitute.For<ISearchService>();
            search
                .SearchInDocumentAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<SearchHit>>([]));
        }

        var ann = Substitute.For<IAnnotationService>();
        ann.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Annotation>>([]));
        var bm = Substitute.For<IBookmarkService>();
        bm.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([]));

        return new DocumentTabViewModel(
            doc, "/tmp/x.pdf", search, ann, bm,
            NullLogger<DocumentTabViewModel>.Instance,
            history);
    }

    [Fact]
    public void Constructor_WithSearchHistory_SeedsRecentSearchesFromService()
    {
        var history = Substitute.For<ISearchHistoryService>();
        history.GetHistory().Returns(new[] { "a", "b", "c" });

        var vm = CreateVmWithHistory(history);

        vm.RecentSearches.Should().Equal(["a", "b", "c"]);
    }

    [Fact]
    public async Task RunSearch_WithSearchHistory_AddsQueryToService()
    {
        var history = Substitute.For<ISearchHistoryService>();
        history.GetHistory().Returns(Array.Empty<string>());
        var vm = CreateVmWithHistory(history);
        vm.SearchText = "foo";

        await vm.RunSearchCommand.ExecuteAsync(null);

        history.Received(1).Add("foo");
    }

    [Fact]
    public async Task RunSearch_WithoutSearchHistory_DoesNotThrow()
    {
        // Без сервиса — никакой Add не вызываем (в коде `?.Add`).
        var search = Substitute.For<ISearchService>();
        search.SearchInDocumentAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<IReadOnlyList<SearchHit>>([]));
        var vm = CreateVm(search: search);
        vm.SearchText = "any";

        var act = async () => await vm.RunSearchCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync();
        vm.RecentSearches.Should().Equal(["any"]);   // legacy per-tab promotion still works
    }

    [Fact]
    public async Task RunSearch_EmptyText_DoesNotCallHistoryAdd()
    {
        var history = Substitute.For<ISearchHistoryService>();
        history.GetHistory().Returns(Array.Empty<string>());
        var vm = CreateVmWithHistory(history);
        vm.SearchText = "";

        await vm.RunSearchCommand.ExecuteAsync(null);

        history.DidNotReceive().Add(Arg.Any<string>());
    }

    // ───── S7/E: IsCurrentPageBookmarked ─────

    [Fact]
    public async Task IsCurrentPageBookmarked_False_WhenNoBookmarks()
    {
        var vm = CreateVm();
        await vm.LoadBookmarksAsync(CancellationToken.None);

        vm.IsCurrentPageBookmarked.Should().BeFalse();
    }

    [Fact]
    public async Task IsCurrentPageBookmarked_True_WhenBookmarkOnCurrentPage()
    {
        var bm = Bookmark.Create(0, "First", DateTimeOffset.UtcNow);
        var bookmarkSvc = Substitute.For<IBookmarkService>();
        bookmarkSvc.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([bm]));
        var vm = CreateVm(bookmarks: bookmarkSvc);
        await vm.LoadBookmarksAsync(CancellationToken.None);

        vm.CurrentPageIndex = 0;

        vm.IsCurrentPageBookmarked.Should().BeTrue();
    }

    [Fact]
    public async Task IsCurrentPageBookmarked_False_WhenBookmarkOnOtherPage()
    {
        var bm = Bookmark.Create(5, "Chapter 2", DateTimeOffset.UtcNow);
        var bookmarkSvc = Substitute.For<IBookmarkService>();
        bookmarkSvc.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([bm]));
        var vm = CreateVm(bookmarks: bookmarkSvc);
        await vm.LoadBookmarksAsync(CancellationToken.None);

        vm.CurrentPageIndex = 0;   // page 0, bookmark is on page 5

        vm.IsCurrentPageBookmarked.Should().BeFalse();
    }

    [Fact]
    public async Task IsCurrentPageBookmarked_UpdatesWhenPageChanges()
    {
        var bm = Bookmark.Create(3, "Mid", DateTimeOffset.UtcNow);
        var bookmarkSvc = Substitute.For<IBookmarkService>();
        bookmarkSvc.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([bm]));
        var vm = CreateVm(bookmarks: bookmarkSvc);
        await vm.LoadBookmarksAsync(CancellationToken.None);

        vm.CurrentPageIndex = 0;
        vm.IsCurrentPageBookmarked.Should().BeFalse();

        vm.CurrentPageIndex = 3;
        vm.IsCurrentPageBookmarked.Should().BeTrue();
    }

    [Fact]
    public async Task IsCurrentPageBookmarked_UpdatesWhenBookmarkAdded()
    {
        var bookmarkSvc = Substitute.For<IBookmarkService>();
        bookmarkSvc.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([]));

        var newBm = Bookmark.Create(0, "New", DateTimeOffset.UtcNow);
        bookmarkSvc.ToggleAsync(Arg.Any<string>(), 0, Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(newBm);

        var vm = CreateVm(bookmarks: bookmarkSvc);
        await vm.LoadBookmarksAsync(CancellationToken.None);
        vm.CurrentPageIndex = 0;

        vm.IsCurrentPageBookmarked.Should().BeFalse();
        await vm.ToggleBookmarkCommand.ExecuteAsync(null);
        vm.IsCurrentPageBookmarked.Should().BeTrue();
    }
}
