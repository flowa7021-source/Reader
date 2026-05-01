using FluentAssertions;
using Foliant.Domain;
using Foliant.ViewModels;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class AnnotationsDocumentViewModelTests
{
    [Fact]
    public void Empty_ConstructionState()
    {
        var vm = new AnnotationsDocumentViewModel(_ => { });

        vm.Groups.Should().BeEmpty();
        vm.TotalCount.Should().Be(0);
        vm.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Rebuild_GroupsByPage_OrdersGroupsByPageIndex()
    {
        var p3 = Annotation.Highlight(3, new AnnotationRect(0, 0, 1, 1), "#000", DateTimeOffset.UtcNow);
        var p1a = Annotation.Highlight(1, new AnnotationRect(0, 0, 1, 1), "#000", DateTimeOffset.UtcNow);
        var p1b = Annotation.StickyNote(1, new AnnotationRect(0, 0, 1, 1), "n", "#000", DateTimeOffset.UtcNow);
        var p7 = Annotation.Freehand(7, [new AnnotationPoint(0, 0)], "#000", DateTimeOffset.UtcNow);

        var vm = new AnnotationsDocumentViewModel(_ => { });
        vm.Rebuild([p3, p1a, p1b, p7]);

        vm.Groups.Select(g => g.PageIndex).Should().Equal([1, 3, 7]);
        vm.Groups.First(g => g.PageIndex == 1).Annotations.Should().HaveCount(2);
        vm.TotalCount.Should().Be(4);
        vm.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Rebuild_AnnotationsInGroup_OrderedByCreatedAt()
    {
        var older = Annotation.Highlight(0, new AnnotationRect(0, 0, 1, 1), "#000",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var newer = Annotation.Highlight(0, new AnnotationRect(0, 0, 1, 1), "#000",
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));

        var vm = new AnnotationsDocumentViewModel(_ => { });
        vm.Rebuild([newer, older]);

        var group = vm.Groups.Single(g => g.PageIndex == 0);
        group.Annotations.Select(a => a.Id).Should().Equal([older.Id, newer.Id]);
    }

    [Fact]
    public void Rebuild_ReplacesPreviousGroups()
    {
        var vm = new AnnotationsDocumentViewModel(_ => { });
        vm.Rebuild([Annotation.Highlight(0, new AnnotationRect(0, 0, 1, 1), "#000", DateTimeOffset.UtcNow)]);
        vm.Groups.Should().HaveCount(1);

        vm.Rebuild([]);

        vm.Groups.Should().BeEmpty();
        vm.TotalCount.Should().Be(0);
    }

    [Fact]
    public void Rebuild_BumpsRefreshTick()
    {
        var vm = new AnnotationsDocumentViewModel(_ => { });
        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        vm.Rebuild([Annotation.Highlight(0, new AnnotationRect(0, 0, 1, 1), "#000", DateTimeOffset.UtcNow)]);

        fired.Should().Contain(nameof(AnnotationsDocumentViewModel.TotalCount));
        fired.Should().Contain(nameof(AnnotationsDocumentViewModel.IsEmpty));
    }

    [Fact]
    public void JumpToAnnotation_InvokesCallback_With_AnnotationPageIndex()
    {
        int? jumped = null;
        var vm = new AnnotationsDocumentViewModel(p => jumped = p);
        var ann = Annotation.Highlight(7, new AnnotationRect(0, 0, 1, 1), "#000", DateTimeOffset.UtcNow);

        vm.JumpToAnnotationCommand.Execute(ann);

        jumped.Should().Be(7);
    }

    [Fact]
    public void JumpToAnnotation_Null_NoOp()
    {
        bool called = false;
        var vm = new AnnotationsDocumentViewModel(_ => called = true);

        vm.JumpToAnnotationCommand.Execute(null);

        called.Should().BeFalse();
    }

    [Fact]
    public void OneBasedPageNumber_Equals_PageIndexPlusOne()
    {
        var group = new AnnotationPageGroup(4, []);
        group.OneBasedPageNumber.Should().Be(5);
    }

    // ───── FilterMode (S10/G) ─────

    [Fact]
    public void FilterMode_DefaultsToAll()
    {
        var vm = new AnnotationsDocumentViewModel(_ => { });
        vm.FilterMode.Should().Be(AnnotationFilterMode.All);
    }

    [Fact]
    public void FilterMode_Highlights_FiltersOutNotesAndFreehand()
    {
        var hl = Annotation.Highlight(0, new AnnotationRect(0, 0, 1, 1), "#000", DateTimeOffset.UtcNow);
        var note = Annotation.StickyNote(0, new AnnotationRect(0, 0, 1, 1), "n", "#000", DateTimeOffset.UtcNow);
        var fh = Annotation.Freehand(0, [new AnnotationPoint(0, 0)], "#000", DateTimeOffset.UtcNow);
        var vm = new AnnotationsDocumentViewModel(_ => { });
        vm.Rebuild([hl, note, fh]);

        vm.FilterMode = AnnotationFilterMode.Highlights;

        vm.TotalCount.Should().Be(1);
        vm.Groups.Single().Annotations.Should().ContainSingle(a => a.Id == hl.Id);
    }

    [Fact]
    public void FilterMode_Notes_FiltersOutHighlightsAndFreehand()
    {
        var hl = Annotation.Highlight(0, new AnnotationRect(0, 0, 1, 1), "#000", DateTimeOffset.UtcNow);
        var note = Annotation.StickyNote(0, new AnnotationRect(0, 0, 1, 1), "n", "#000", DateTimeOffset.UtcNow);
        var fh = Annotation.Freehand(0, [new AnnotationPoint(0, 0)], "#000", DateTimeOffset.UtcNow);
        var vm = new AnnotationsDocumentViewModel(_ => { });
        vm.Rebuild([hl, note, fh]);

        vm.FilterMode = AnnotationFilterMode.Notes;

        vm.TotalCount.Should().Be(1);
        vm.Groups.Single().Annotations.Should().ContainSingle(a => a.Id == note.Id);
    }

    [Fact]
    public void FilterMode_Freehand_FiltersOutHighlightsAndNotes()
    {
        var hl = Annotation.Highlight(0, new AnnotationRect(0, 0, 1, 1), "#000", DateTimeOffset.UtcNow);
        var note = Annotation.StickyNote(0, new AnnotationRect(0, 0, 1, 1), "n", "#000", DateTimeOffset.UtcNow);
        var fh = Annotation.Freehand(0, [new AnnotationPoint(0, 0)], "#000", DateTimeOffset.UtcNow);
        var vm = new AnnotationsDocumentViewModel(_ => { });
        vm.Rebuild([hl, note, fh]);

        vm.FilterMode = AnnotationFilterMode.Freehand;

        vm.TotalCount.Should().Be(1);
        vm.Groups.Single().Annotations.Should().ContainSingle(a => a.Id == fh.Id);
    }

    [Fact]
    public void FilterMode_AllAfterHighlights_RestoresFullList()
    {
        var hl = Annotation.Highlight(0, new AnnotationRect(0, 0, 1, 1), "#000", DateTimeOffset.UtcNow);
        var note = Annotation.StickyNote(0, new AnnotationRect(0, 0, 1, 1), "n", "#000", DateTimeOffset.UtcNow);
        var vm = new AnnotationsDocumentViewModel(_ => { });
        vm.Rebuild([hl, note]);

        vm.FilterMode = AnnotationFilterMode.Highlights;
        vm.TotalCount.Should().Be(1);

        vm.FilterMode = AnnotationFilterMode.All;
        vm.TotalCount.Should().Be(2);
    }

    // ───── SearchText filter (S10/H) ─────

    [Fact]
    public void SearchText_DefaultEmpty_NoFilterApplied()
    {
        var hl = Annotation.Highlight(0, new AnnotationRect(0, 0, 1, 1), "#000", DateTimeOffset.UtcNow);
        var note = Annotation.StickyNote(0, new AnnotationRect(0, 0, 1, 1), "anything", "#000", DateTimeOffset.UtcNow);
        var vm = new AnnotationsDocumentViewModel(_ => { });
        vm.Rebuild([hl, note]);

        vm.SearchText.Should().Be(string.Empty);
        vm.TotalCount.Should().Be(2);
    }

    [Fact]
    public void SearchText_NonEmpty_FiltersByNoteText_CaseInsensitive()
    {
        var t = DateTimeOffset.UtcNow;
        var n1 = Annotation.StickyNote(0, new AnnotationRect(0, 0, 1, 1), "TODO: investigate", "#000", t);
        var n2 = Annotation.StickyNote(0, new AnnotationRect(0, 0, 1, 1), "leave me", "#000", t);
        var n3 = Annotation.StickyNote(1, new AnnotationRect(0, 0, 1, 1), "another todo here", "#000", t);
        var vm = new AnnotationsDocumentViewModel(_ => { });
        vm.Rebuild([n1, n2, n3]);

        vm.SearchText = "todo";

        vm.TotalCount.Should().Be(2);
        vm.Groups.SelectMany(g => g.Annotations).Select(a => a.Id)
            .Should().BeEquivalentTo(new[] { n1.Id, n3.Id });
    }

    [Fact]
    public void SearchText_NonEmpty_ExcludesAnnotationsWithoutText()
    {
        var hl = Annotation.Highlight(0, new AnnotationRect(0, 0, 1, 1), "#000", DateTimeOffset.UtcNow);
        var note = Annotation.StickyNote(0, new AnnotationRect(0, 0, 1, 1), "search-me", "#000", DateTimeOffset.UtcNow);
        var fh = Annotation.Freehand(0, [new AnnotationPoint(0, 0)], "#000", DateTimeOffset.UtcNow);
        var vm = new AnnotationsDocumentViewModel(_ => { });
        vm.Rebuild([hl, note, fh]);

        vm.SearchText = "search";

        vm.TotalCount.Should().Be(1);
        vm.Groups.Single().Annotations.Single().Id.Should().Be(note.Id);
    }

    [Fact]
    public void SearchText_Whitespace_TreatedAsEmpty()
    {
        var note = Annotation.StickyNote(0, new AnnotationRect(0, 0, 1, 1), "x", "#000", DateTimeOffset.UtcNow);
        var hl = Annotation.Highlight(0, new AnnotationRect(0, 0, 1, 1), "#000", DateTimeOffset.UtcNow);
        var vm = new AnnotationsDocumentViewModel(_ => { });
        vm.Rebuild([note, hl]);

        vm.SearchText = "    ";

        vm.TotalCount.Should().Be(2);
    }

    [Fact]
    public void SearchText_AndFilterMode_AppliedTogether()
    {
        var t = DateTimeOffset.UtcNow;
        var n1 = Annotation.StickyNote(0, new AnnotationRect(0, 0, 1, 1), "find-me note", "#000", t);
        var n2 = Annotation.StickyNote(0, new AnnotationRect(0, 0, 1, 1), "different", "#000", t);
        var vm = new AnnotationsDocumentViewModel(_ => { });
        vm.Rebuild([n1, n2]);

        vm.FilterMode = AnnotationFilterMode.Notes;
        vm.SearchText = "find";

        vm.TotalCount.Should().Be(1);
        vm.Groups.Single().Annotations.Single().Id.Should().Be(n1.Id);
    }

    [Fact]
    public void SearchText_Change_RaisesPropertyChanged()
    {
        var note = Annotation.StickyNote(0, new AnnotationRect(0, 0, 1, 1), "x", "#000", DateTimeOffset.UtcNow);
        var vm = new AnnotationsDocumentViewModel(_ => { });
        vm.Rebuild([note]);

        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        vm.SearchText = "y";

        fired.Should().Contain(nameof(AnnotationsDocumentViewModel.SearchText));
        fired.Should().Contain(nameof(AnnotationsDocumentViewModel.TotalCount));
        fired.Should().Contain(nameof(AnnotationsDocumentViewModel.IsEmpty));
    }

    // ───── Sort modes (S10/I) ─────

    [Fact]
    public void SortPageDescending_ReversesGroupOrder()
    {
        var p1 = Annotation.Highlight(0, new AnnotationRect(0, 0, 1, 1), "#000", DateTimeOffset.UtcNow);
        var p3 = Annotation.Highlight(2, new AnnotationRect(0, 0, 1, 1), "#000", DateTimeOffset.UtcNow);
        var p7 = Annotation.Highlight(6, new AnnotationRect(0, 0, 1, 1), "#000", DateTimeOffset.UtcNow);
        var vm = new AnnotationsDocumentViewModel(_ => { });
        vm.Rebuild([p1, p3, p7]);

        vm.SortPageDescending = true;

        vm.Groups.Select(g => g.PageIndex).Should().Equal([6, 2, 0]);
    }

    [Fact]
    public void SortWithinGroupNewestFirst_ReversesAnnotationOrder()
    {
        var older = Annotation.Highlight(0, new AnnotationRect(0, 0, 1, 1), "#000",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var newer = Annotation.Highlight(0, new AnnotationRect(0, 0, 1, 1), "#000",
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var vm = new AnnotationsDocumentViewModel(_ => { });
        vm.Rebuild([older, newer]);

        vm.SortWithinGroupNewestFirst = true;

        var group = vm.Groups.Single();
        group.Annotations.Select(a => a.Id).Should().Equal([newer.Id, older.Id]);
    }

    [Fact]
    public void Sort_BothFlagsSet_OrdersAccordingly()
    {
        var p0a = Annotation.Highlight(0, new AnnotationRect(0, 0, 1, 1), "#000",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var p0b = Annotation.Highlight(0, new AnnotationRect(0, 0, 1, 1), "#000",
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var p5 = Annotation.Highlight(5, new AnnotationRect(0, 0, 1, 1), "#000", DateTimeOffset.UtcNow);
        var vm = new AnnotationsDocumentViewModel(_ => { });
        vm.Rebuild([p0a, p0b, p5]);

        vm.SortPageDescending = true;
        vm.SortWithinGroupNewestFirst = true;

        vm.Groups.Select(g => g.PageIndex).Should().Equal([5, 0]);
        vm.Groups.Last().Annotations.Select(a => a.Id).Should().Equal([p0b.Id, p0a.Id]);
    }

    [Fact]
    public void Sort_DefaultsAreAscending()
    {
        var vm = new AnnotationsDocumentViewModel(_ => { });

        vm.SortPageDescending.Should().BeFalse();
        vm.SortWithinGroupNewestFirst.Should().BeFalse();
    }

    [Fact]
    public void Sort_TogglingBack_RestoresOriginalOrder()
    {
        var p1 = Annotation.Highlight(0, new AnnotationRect(0, 0, 1, 1), "#000", DateTimeOffset.UtcNow);
        var p3 = Annotation.Highlight(2, new AnnotationRect(0, 0, 1, 1), "#000", DateTimeOffset.UtcNow);
        var vm = new AnnotationsDocumentViewModel(_ => { });
        vm.Rebuild([p1, p3]);

        vm.SortPageDescending = true;
        vm.Groups.Select(g => g.PageIndex).Should().Equal([2, 0]);

        vm.SortPageDescending = false;
        vm.Groups.Select(g => g.PageIndex).Should().Equal([0, 2]);
    }

    [Fact]
    public void Sort_Change_RaisesPropertyChanged()
    {
        var note = Annotation.StickyNote(0, new AnnotationRect(0, 0, 1, 1), "x", "#000", DateTimeOffset.UtcNow);
        var vm = new AnnotationsDocumentViewModel(_ => { });
        vm.Rebuild([note]);

        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        vm.SortPageDescending = true;
        fired.Should().Contain(nameof(AnnotationsDocumentViewModel.SortPageDescending));

        fired.Clear();
        vm.SortWithinGroupNewestFirst = true;
        fired.Should().Contain(nameof(AnnotationsDocumentViewModel.SortWithinGroupNewestFirst));
    }

    [Fact]
    public void FilterMode_Change_RaisesPropertyChanged()
    {
        var vm = new AnnotationsDocumentViewModel(_ => { });
        vm.Rebuild([Annotation.Highlight(0, new AnnotationRect(0, 0, 1, 1), "#000", DateTimeOffset.UtcNow)]);

        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        vm.FilterMode = AnnotationFilterMode.Notes;

        fired.Should().Contain(nameof(AnnotationsDocumentViewModel.FilterMode));
        fired.Should().Contain(nameof(AnnotationsDocumentViewModel.TotalCount));
        fired.Should().Contain(nameof(AnnotationsDocumentViewModel.IsEmpty));
    }
}
