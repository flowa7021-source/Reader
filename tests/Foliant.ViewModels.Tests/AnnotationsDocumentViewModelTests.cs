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
