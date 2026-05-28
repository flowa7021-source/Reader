using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Application.Settings;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelAnnotationTests
{
    private static DocumentTabViewModel CreateVm(IAnnotationService annotations, ISettingsService? settings = null)
    {
        var doc = Substitute.For<IDocument>();
        doc.PageCount.Returns(1);

        return new DocumentTabViewModel(
            doc,
            "/tmp/doc.pdf",
            Substitute.For<ISearchService>(),
            annotations,
            Substitute.For<IBookmarkService>(),
            NullLogger<DocumentTabViewModel>.Instance,
            settings: settings);
    }

    private static ISettingsService SettingsWithAuthor(string? author)
    {
        var s = Substitute.For<ISettingsService>();
        s.Current.Returns(AppSettings.Default with { DefaultAnnotationAuthor = author });
        return s;
    }

    [Fact]
    public async Task AddNoteAt_CreatesStickyNoteOnCurrentPage()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(ann);

        await vm.AddNoteAtCommand.ExecuteAsync(new AnnotationPoint(100, 700));

        vm.CurrentPageAnnotations.Should().ContainSingle(a => a.Kind == AnnotationKind.StickyNote);
        await ann.Received(1).AddAsync(
            "/tmp/doc.pdf",
            Arg.Is<Annotation>(a => a.Kind == AnnotationKind.StickyNote && a.PageIndex == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddNoteAt_NullLocation_IsNoOp()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(ann);

        await vm.AddNoteAtCommand.ExecuteAsync(null);

        vm.CurrentPageAnnotations.Should().BeEmpty();
        await ann.DidNotReceive().AddAsync(Arg.Any<string>(), Arg.Any<Annotation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddNoteAtPage_CreatesStickyNoteOnSpecifiedPage_NotCurrent()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(ann); // CurrentPageIndex == 0

        await vm.AddNoteAtPageAsync(2, new AnnotationPoint(100, 700));

        await ann.Received(1).AddAsync(
            "/tmp/doc.pdf",
            Arg.Is<Annotation>(a => a.Kind == AnnotationKind.StickyNote && a.PageIndex == 2),
            Arg.Any<CancellationToken>());
        // Page 2 != current page 0, so it must NOT appear in the current-page snapshot.
        vm.CurrentPageAnnotations.Should().BeEmpty();
    }

    [Fact]
    public async Task AddNoteAtPage_NullLocation_IsNoOp()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(ann);

        await vm.AddNoteAtPageAsync(1, null);

        await ann.DidNotReceive().AddAsync(Arg.Any<string>(), Arg.Any<Annotation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SelectAnnotationTool_SetsActiveTool_AndTogglesOff()
    {
        var vm = CreateVm(Substitute.For<IAnnotationService>());

        vm.SelectAnnotationToolCommand.Execute(AnnotationKind.Freehand);
        vm.ActiveAnnotationTool.Should().Be(AnnotationKind.Freehand);

        vm.SelectAnnotationToolCommand.Execute(AnnotationKind.Highlight);
        vm.ActiveAnnotationTool.Should().Be(AnnotationKind.Highlight);

        vm.SelectAnnotationToolCommand.Execute(AnnotationKind.Highlight);
        vm.ActiveAnnotationTool.Should().BeNull();
    }

    [Fact]
    public void ClearAnnotationTool_ResetsActiveTool()
    {
        var vm = CreateVm(Substitute.For<IAnnotationService>());
        vm.SelectAnnotationToolCommand.Execute(AnnotationKind.StickyNote);

        vm.ClearAnnotationToolCommand.Execute(null);

        vm.ActiveAnnotationTool.Should().BeNull();
    }

    [Fact]
    public async Task RemoveAnnotation_CallsService_AndFiresCountChange()
    {
        var ann = Substitute.For<IAnnotationService>();
        ann.RemoveAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var vm = CreateVm(ann);
        await vm.AddNoteAtCommand.ExecuteAsync(new AnnotationPoint(50, 600));
        var note = vm.CurrentPageAnnotations.Single();

        int countChanges = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DocumentTabViewModel.TotalAnnotationsCount))
            {
                countChanges++;
            }
        };

        await vm.RemoveAnnotationCommand.ExecuteAsync(note);

        await ann.Received(1).RemoveAsync("/tmp/doc.pdf", note.Id, Arg.Any<CancellationToken>());
        vm.CurrentPageAnnotations.Should().BeEmpty();
        vm.TotalAnnotationsCount.Should().Be(0);
        countChanges.Should().Be(1);
        vm.AnnotationsDocument.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task EditNoteText_UpdatesTextViaService()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(ann);
        await vm.AddNoteAtCommand.ExecuteAsync(new AnnotationPoint(10, 200));
        var note = vm.CurrentPageAnnotations.Single();

        await vm.EditNoteTextCommand.ExecuteAsync((note, "Revised"));

        await ann.Received(1).UpdateAsync(
            "/tmp/doc.pdf",
            Arg.Is<Annotation>(a => a.Id == note.Id && a.Text == "Revised"),
            Arg.Any<CancellationToken>());
        vm.CurrentPageAnnotations.Single().Text.Should().Be("Revised");
    }

    [Fact]
    public async Task EditNoteText_UnchangedText_IsNoOp()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(ann);
        await vm.AddNoteAtCommand.ExecuteAsync(new AnnotationPoint(10, 200));
        var note = vm.CurrentPageAnnotations.Single();

        await vm.EditNoteTextCommand.ExecuteAsync((note, note.Text!));

        await ann.DidNotReceive().UpdateAsync(Arg.Any<string>(), Arg.Any<Annotation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddFreehand_PersistsAndIncrementsCount()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(ann);
        var points = new List<AnnotationPoint>
        {
            new(10, 10),
            new(20, 25),
            new(30, 40),
        };

        await vm.AddFreehandAsync(0, points, "#FF0000", CancellationToken.None);

        await ann.Received(1).AddAsync(
            "/tmp/doc.pdf",
            Arg.Is<Annotation>(a => a.Kind == AnnotationKind.Freehand && a.InkPoints!.Count == 3),
            Arg.Any<CancellationToken>());
        vm.FreehandCount.Should().Be(1);
        vm.TotalAnnotationsCount.Should().Be(1);
        vm.CurrentPageAnnotations.Should().ContainSingle(a => a.Kind == AnnotationKind.Freehand);
    }

    [Fact]
    public async Task AddFreehand_FewerThanTwoPoints_IsNoOp()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(ann);

        await vm.AddFreehandAsync(0, [new AnnotationPoint(5, 5)], "#FF0000", CancellationToken.None);

        await ann.DidNotReceive().AddAsync(Arg.Any<string>(), Arg.Any<Annotation>(), Arg.Any<CancellationToken>());
        vm.FreehandCount.Should().Be(0);
    }

    [Fact]
    public async Task AddHighlight_StampsAuthorFromSettings()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(ann, SettingsWithAuthor("Иван Петров"));

        await vm.AddHighlightAsync(0, new AnnotationRect(0, 0, 10, 10), "#FFEB3B", default);

        await ann.Received().AddAsync(Arg.Any<string>(),
            Arg.Is<Annotation>(a => a.Author == "Иван Петров"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddNote_StampsAuthorFromSettings()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(ann, SettingsWithAuthor("Reviewer"));

        await vm.AddNoteAsync(0, new AnnotationRect(0, 0, 10, 10), "hi", "#FFEB3B", default);

        await ann.Received().AddAsync(Arg.Any<string>(),
            Arg.Is<Annotation>(a => a.Author == "Reviewer"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddFreehand_StampsAuthorFromSettings()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(ann, SettingsWithAuthor("Reviewer"));

        await vm.AddFreehandAsync(0,
            [new AnnotationPoint(0, 0), new AnnotationPoint(5, 5)],
            "#000000", default);

        await ann.Received().AddAsync(Arg.Any<string>(),
            Arg.Is<Annotation>(a => a.Author == "Reviewer"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddHighlight_WhenSettingsAuthorIsBlank_AuthorIsNull()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(ann, SettingsWithAuthor("   "));

        await vm.AddHighlightAsync(0, new AnnotationRect(0, 0, 10, 10), "#FFEB3B", default);

        await ann.Received().AddAsync(Arg.Any<string>(),
            Arg.Is<Annotation>(a => a.Author == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EditNoteText_BumpsModifiedAt()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(ann);
        var note = Annotation.StickyNote(0, new AnnotationRect(0, 0, 10, 10), "old", "#FF0", DateTimeOffset.UnixEpoch);
        await vm.AddNoteAsync(note.PageIndex, note.Bounds!, note.Text!, note.ColorHex, default);
        var seeded = vm.CurrentPageAnnotations.Should().ContainSingle().Subject;

        var before = DateTimeOffset.UtcNow;
        await vm.EditNoteTextCommand.ExecuteAsync((seeded, "new"));

        await ann.Received().UpdateAsync(Arg.Any<string>(),
            Arg.Is<Annotation>(a => a.Text == "new" && a.ModifiedAt != null && a.ModifiedAt >= before),
            Arg.Any<CancellationToken>());
    }
}
