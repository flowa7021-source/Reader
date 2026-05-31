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

    [Fact]
    public async Task EditNoteSubject_SetsSubjectAndBumpsModifiedAt()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(ann);
        var note = Annotation.StickyNote(0, new AnnotationRect(0, 0, 10, 10), "txt", "#FF0", DateTimeOffset.UnixEpoch);
        await vm.AddNoteAsync(note.PageIndex, note.Bounds!, note.Text!, note.ColorHex, default);
        var seeded = vm.CurrentPageAnnotations.Should().ContainSingle().Subject;

        var before = DateTimeOffset.UtcNow;
        await vm.EditNoteSubjectCommand.ExecuteAsync((seeded, (string?)"Important"));

        await ann.Received().UpdateAsync(Arg.Any<string>(),
            Arg.Is<Annotation>(a => a.Subject == "Important" && a.ModifiedAt != null && a.ModifiedAt >= before),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EditNoteSubject_WhitespaceInput_NormalizesToNull()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(ann);
        var note = Annotation.StickyNote(0, new AnnotationRect(0, 0, 10, 10), "txt", "#FF0", DateTimeOffset.UnixEpoch) with
        {
            Subject = "Existing",
        };
        await vm.AddNoteAsync(note.PageIndex, note.Bounds!, note.Text!, note.ColorHex, default);
        // The seeded note doesn't carry the Subject (AddNote constructs a fresh one). Override
        // via UpdateAnnotation to mirror "user set a subject earlier" state.
        var seeded = vm.CurrentPageAnnotations.Should().ContainSingle().Subject with { Subject = "Existing" };
        await vm.UpdateAnnotationCommand.ExecuteAsync(seeded);

        await vm.EditNoteSubjectCommand.ExecuteAsync((seeded, (string?)"   "));

        await ann.Received().UpdateAsync(Arg.Any<string>(),
            Arg.Is<Annotation>(a => a.Subject == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EditNoteSubject_UnchangedValue_IsNoOp()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(ann);
        var note = Annotation.StickyNote(0, new AnnotationRect(0, 0, 10, 10), "txt", "#FF0", DateTimeOffset.UnixEpoch);
        await vm.AddNoteAsync(note.PageIndex, note.Bounds!, note.Text!, note.ColorHex, default);
        var seeded = vm.CurrentPageAnnotations.Should().ContainSingle().Subject;

        ann.ClearReceivedCalls();

        // seeded.Subject is null; passing null / whitespace should be no-op.
        await vm.EditNoteSubjectCommand.ExecuteAsync((seeded, (string?)null));

        await ann.DidNotReceive().UpdateAsync(Arg.Any<string>(), Arg.Any<Annotation>(), Arg.Any<CancellationToken>());
    }

    // ───── Underline / Strikethrough (Q-F16 D7) ─────

    [Fact]
    public async Task AddUnderline_CreatesUnderlineOnCurrentPage()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(ann);
        var bounds = new AnnotationRect(1, 2, 3, 4);

        await vm.AddUnderlineAsync(0, bounds, "#FF0000", CancellationToken.None);

        await ann.Received(1).AddAsync(
            "/tmp/doc.pdf",
            Arg.Is<Annotation>(a => a.Kind == AnnotationKind.Underline && a.Bounds == bounds && a.ColorHex == "#FF0000"),
            Arg.Any<CancellationToken>());
        vm.UnderlineCount.Should().Be(1);
        vm.TotalAnnotationsCount.Should().Be(1);
        vm.CurrentPageAnnotations.Should().ContainSingle(a => a.Kind == AnnotationKind.Underline);
    }

    [Fact]
    public async Task AddStrikethrough_CreatesStrikethroughOnCurrentPage()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(ann);
        var bounds = new AnnotationRect(5, 6, 7, 8);

        await vm.AddStrikethroughAsync(0, bounds, "#00FF00", CancellationToken.None);

        await ann.Received(1).AddAsync(
            "/tmp/doc.pdf",
            Arg.Is<Annotation>(a => a.Kind == AnnotationKind.Strikethrough && a.Bounds == bounds && a.ColorHex == "#00FF00"),
            Arg.Any<CancellationToken>());
        vm.StrikethroughCount.Should().Be(1);
        vm.TotalAnnotationsCount.Should().Be(1);
        vm.CurrentPageAnnotations.Should().ContainSingle(a => a.Kind == AnnotationKind.Strikethrough);
    }

    [Fact]
    public async Task AddUnderline_OnDifferentPage_NotInCurrentPageList()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(ann);

        await vm.AddUnderlineAsync(5, new AnnotationRect(0, 0, 10, 10), "#000", default);

        vm.TotalAnnotationsCount.Should().Be(1);
        vm.UnderlineCount.Should().Be(1);
        vm.CurrentPageAnnotations.Should().BeEmpty();
    }

    [Fact]
    public async Task AddUnderline_StampsAuthorFromSettings()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(ann, SettingsWithAuthor("Alice"));

        await vm.AddUnderlineAsync(0, new AnnotationRect(0, 0, 10, 10), "#FF0", default);

        await ann.Received(1).AddAsync(
            Arg.Any<string>(),
            Arg.Is<Annotation>(a => a.Author == "Alice"),
            Arg.Any<CancellationToken>());
    }

    // ───── Domain factories ─────

    [Fact]
    public void DomainUnderlineFactory_SetsKindAndBounds()
    {
        var b = new AnnotationRect(1, 2, 3, 4);
        var a = Annotation.Underline(0, b, "#000", DateTimeOffset.UnixEpoch);
        a.Kind.Should().Be(AnnotationKind.Underline);
        a.Bounds.Should().Be(b);
        a.Text.Should().BeNull();
        a.InkPoints.Should().BeNull();
    }

    [Fact]
    public void DomainStrikethroughFactory_SetsKindAndBounds()
    {
        var b = new AnnotationRect(1, 2, 3, 4);
        var a = Annotation.Strikethrough(0, b, "#000", DateTimeOffset.UnixEpoch);
        a.Kind.Should().Be(AnnotationKind.Strikethrough);
        a.Bounds.Should().Be(b);
        a.Text.Should().BeNull();
        a.InkPoints.Should().BeNull();
    }

    // ───── Geometric shapes (Q-F16, Track R3) ─────

    [Fact]
    public async Task AddRectangle_DispatchesToService()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(ann);
        var bounds = new AnnotationRect(10, 20, 100, 50);

        await vm.AddRectangleAsync(0, bounds, "#FF0000", CancellationToken.None);

        await ann.Received(1).AddAsync(
            "/tmp/doc.pdf",
            Arg.Is<Annotation>(a => a.Kind == AnnotationKind.Rectangle && a.Bounds == bounds),
            Arg.Any<CancellationToken>());
        vm.RectangleCount.Should().Be(1);
        vm.CurrentPageAnnotations.Should().ContainSingle(a => a.Kind == AnnotationKind.Rectangle);
    }

    [Fact]
    public async Task AddEllipse_DispatchesToService()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(ann);
        var bounds = new AnnotationRect(0, 0, 50, 30);

        await vm.AddEllipseAsync(0, bounds, "#00FF00", CancellationToken.None);

        await ann.Received(1).AddAsync(
            "/tmp/doc.pdf",
            Arg.Is<Annotation>(a => a.Kind == AnnotationKind.Ellipse && a.Bounds == bounds),
            Arg.Any<CancellationToken>());
        vm.EllipseCount.Should().Be(1);
    }

    [Fact]
    public async Task AddLine_TwoPoints_Dispatches()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(ann);
        var points = new List<AnnotationPoint> { new(0, 0), new(100, 100) };

        await vm.AddLineAsync(0, points, "#0000FF", CancellationToken.None);

        await ann.Received(1).AddAsync(
            "/tmp/doc.pdf",
            Arg.Is<Annotation>(a => a.Kind == AnnotationKind.Line && a.InkPoints!.Count == 2),
            Arg.Any<CancellationToken>());
        vm.LineCount.Should().Be(1);
    }

    [Fact]
    public async Task AddLine_WrongPointCount_IsNoOp()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(ann);

        await vm.AddLineAsync(0, [new AnnotationPoint(1, 1)], "#000", CancellationToken.None);

        await ann.DidNotReceive().AddAsync(Arg.Any<string>(), Arg.Any<Annotation>(), Arg.Any<CancellationToken>());
        vm.LineCount.Should().Be(0);
    }

    [Fact]
    public async Task AddArrow_TwoPoints_Dispatches()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(ann);
        var points = new List<AnnotationPoint> { new(0, 0), new(50, 25) };

        await vm.AddArrowAsync(0, points, "#FF00FF", CancellationToken.None);

        await ann.Received(1).AddAsync(
            "/tmp/doc.pdf",
            Arg.Is<Annotation>(a => a.Kind == AnnotationKind.Arrow && a.InkPoints!.Count == 2),
            Arg.Any<CancellationToken>());
        vm.ArrowCount.Should().Be(1);
    }

    [Fact]
    public async Task AddPolygon_ThreePoints_Dispatches()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(ann);
        var points = new List<AnnotationPoint> { new(0, 0), new(10, 0), new(5, 10) };

        await vm.AddPolygonAsync(0, points, "#FFFF00", CancellationToken.None);

        await ann.Received(1).AddAsync(
            "/tmp/doc.pdf",
            Arg.Is<Annotation>(a => a.Kind == AnnotationKind.Polygon && a.InkPoints!.Count == 3),
            Arg.Any<CancellationToken>());
        vm.PolygonCount.Should().Be(1);
    }

    [Fact]
    public async Task AddPolygon_FewerThanThreePoints_IsNoOp()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(ann);
        var points = new List<AnnotationPoint> { new(0, 0), new(10, 10) };

        await vm.AddPolygonAsync(0, points, "#000", CancellationToken.None);

        await ann.DidNotReceive().AddAsync(Arg.Any<string>(), Arg.Any<Annotation>(), Arg.Any<CancellationToken>());
        vm.PolygonCount.Should().Be(0);
    }

    [Fact]
    public async Task AddRectangle_OnDifferentPage_NotInCurrentList()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(ann);

        await vm.AddRectangleAsync(5, new AnnotationRect(0, 0, 10, 10), "#000", default);

        vm.TotalAnnotationsCount.Should().Be(1);
        vm.RectangleCount.Should().Be(1);
        vm.CurrentPageAnnotations.Should().BeEmpty();
    }

    [Fact]
    public async Task AddRectangle_StampsAuthorFromSettings()
    {
        var ann = Substitute.For<IAnnotationService>();
        var vm = CreateVm(ann, SettingsWithAuthor("Charlie"));

        await vm.AddRectangleAsync(0, new AnnotationRect(0, 0, 10, 10), "#000", default);

        await ann.Received(1).AddAsync(
            Arg.Any<string>(),
            Arg.Is<Annotation>(a => a.Author == "Charlie"),
            Arg.Any<CancellationToken>());
    }

    // ───── Domain factory contracts ─────

    [Fact]
    public void DomainRectangleFactory_SetsKindAndBounds()
    {
        var b = new AnnotationRect(1, 2, 3, 4);
        var a = Annotation.Rectangle(0, b, "#000", DateTimeOffset.UnixEpoch);
        a.Kind.Should().Be(AnnotationKind.Rectangle);
        a.Bounds.Should().Be(b);
        a.InkPoints.Should().BeNull();
    }

    [Fact]
    public void DomainEllipseFactory_SetsKindAndBounds()
    {
        var b = new AnnotationRect(1, 2, 3, 4);
        var a = Annotation.Ellipse(0, b, "#000", DateTimeOffset.UnixEpoch);
        a.Kind.Should().Be(AnnotationKind.Ellipse);
        a.Bounds.Should().Be(b);
    }

    [Fact]
    public void DomainLineFactory_ExactlyTwoPoints_OK()
    {
        var pts = new List<AnnotationPoint> { new(0, 0), new(10, 10) };
        var a = Annotation.Line(0, pts, "#000", DateTimeOffset.UnixEpoch);
        a.Kind.Should().Be(AnnotationKind.Line);
        a.Bounds.Should().BeNull();
        a.InkPoints.Should().BeEquivalentTo(pts);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void DomainLineFactory_WrongPointCount_Throws(int n)
    {
        var pts = Enumerable.Range(0, n).Select(i => new AnnotationPoint(i, i)).ToList();
        Action act = () => Annotation.Line(0, pts, "#000", DateTimeOffset.UnixEpoch);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void DomainArrowFactory_WrongPointCount_Throws(int n)
    {
        var pts = Enumerable.Range(0, n).Select(i => new AnnotationPoint(i, i)).ToList();
        Action act = () => Annotation.Arrow(0, pts, "#000", DateTimeOffset.UnixEpoch);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void DomainPolygonFactory_FewerThanThreePoints_Throws(int n)
    {
        var pts = Enumerable.Range(0, n).Select(i => new AnnotationPoint(i, i)).ToList();
        Action act = () => Annotation.Polygon(0, pts, "#000", DateTimeOffset.UnixEpoch);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DomainPolygonFactory_ThreeOrMore_OK()
    {
        var pts = new List<AnnotationPoint> { new(0, 0), new(10, 0), new(5, 10) };
        var a = Annotation.Polygon(0, pts, "#000", DateTimeOffset.UnixEpoch);
        a.Kind.Should().Be(AnnotationKind.Polygon);
        a.InkPoints.Should().HaveCount(3);
    }
}
