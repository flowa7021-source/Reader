using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelToolsTests
{
    private static DocumentTabViewModel CreateVm(IAnnotationService? annotations = null)
    {
        var document = Substitute.For<IDocument>();
        document.PageCount.Returns(3);
        document.Metadata.Returns(new DocumentMetadata(null, null, null, null, null, new Dictionary<string, string>()));

        var search = Substitute.For<ISearchService>();
        search.SearchInDocumentAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<IReadOnlyList<SearchHit>>([]));
        annotations ??= Substitute.For<IAnnotationService>();
        annotations.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(Task.FromResult<IReadOnlyList<Annotation>>([]));
        annotations.AddAsync(Arg.Any<string>(), Arg.Any<Annotation>(), Arg.Any<CancellationToken>())
                   .Returns(ci => Task.FromResult((Annotation)ci[1]));
        var bm = Substitute.For<IBookmarkService>();
        bm.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([]));

        return new DocumentTabViewModel(document, "/tmp/x.pdf", search, annotations, bm, NullLogger<DocumentTabViewModel>.Instance);
    }

    private static IAnnotationService EchoService()
    {
        var service = Substitute.For<IAnnotationService>();
        service.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<IReadOnlyList<Annotation>>([]));
        service.AddAsync(Arg.Any<string>(), Arg.Any<Annotation>(), Arg.Any<CancellationToken>())
               .Returns(ci => Task.FromResult((Annotation)ci[1]));
        return service;
    }

    [Fact]
    public void Default_ActiveToolIsNone_NotCreating()
    {
        var vm = CreateVm();

        vm.ActiveTool.Should().Be(AnnotationTool.None);
        vm.IsAnnotationCreationActive.Should().BeFalse();
        vm.ActiveToolGesture.Should().Be(AnnotationToolGesture.None);
    }

    [Theory]
    [InlineData(AnnotationTool.Highlight, AnnotationToolGesture.RubberBandRect)]
    [InlineData(AnnotationTool.Underline, AnnotationToolGesture.RubberBandRect)]
    [InlineData(AnnotationTool.Strikethrough, AnnotationToolGesture.RubberBandRect)]
    [InlineData(AnnotationTool.Rectangle, AnnotationToolGesture.RubberBandRect)]
    [InlineData(AnnotationTool.Ellipse, AnnotationToolGesture.RubberBandRect)]
    [InlineData(AnnotationTool.Stamp, AnnotationToolGesture.RubberBandRect)]
    [InlineData(AnnotationTool.Line, AnnotationToolGesture.TwoPoint)]
    [InlineData(AnnotationTool.Arrow, AnnotationToolGesture.TwoPoint)]
    [InlineData(AnnotationTool.Freehand, AnnotationToolGesture.MultiPoint)]
    [InlineData(AnnotationTool.Polygon, AnnotationToolGesture.MultiPoint)]
    [InlineData(AnnotationTool.StickyNote, AnnotationToolGesture.SingleClick)]
    [InlineData(AnnotationTool.None, AnnotationToolGesture.None)]
    public void GestureFor_MapsEachToolToItsGesture(AnnotationTool tool, AnnotationToolGesture expected)
    {
        DocumentTabViewModel.GestureFor(tool).Should().Be(expected);
    }

    [Fact]
    public void SelectTool_SetsActiveTool()
    {
        var vm = CreateVm();

        vm.SelectToolCommand.Execute(AnnotationTool.Rectangle);

        vm.ActiveTool.Should().Be(AnnotationTool.Rectangle);
        vm.IsAnnotationCreationActive.Should().BeTrue();
        vm.ActiveToolGesture.Should().Be(AnnotationToolGesture.RubberBandRect);
    }

    [Fact]
    public void SelectTool_SameToolTwice_TogglesToNone()
    {
        var vm = CreateVm();

        vm.SelectToolCommand.Execute(AnnotationTool.Ellipse);
        vm.SelectToolCommand.Execute(AnnotationTool.Ellipse);

        vm.ActiveTool.Should().Be(AnnotationTool.None);
    }

    [Fact]
    public void ClearTool_ResetsToNone()
    {
        var vm = CreateVm();
        vm.SelectToolCommand.Execute(AnnotationTool.Line);

        vm.ClearToolCommand.Execute(null);

        vm.ActiveTool.Should().Be(AnnotationTool.None);
    }

    [Theory]
    [InlineData(AnnotationTool.Highlight, AnnotationKind.Highlight)]
    [InlineData(AnnotationTool.Underline, AnnotationKind.Underline)]
    [InlineData(AnnotationTool.Strikethrough, AnnotationKind.Strikethrough)]
    [InlineData(AnnotationTool.Rectangle, AnnotationKind.Rectangle)]
    [InlineData(AnnotationTool.Ellipse, AnnotationKind.Ellipse)]
    public async Task CommitRectTool_CreatesMatchingAnnotation(AnnotationTool tool, AnnotationKind kind)
    {
        var service = EchoService();
        var vm = CreateVm(service);
        vm.SelectToolCommand.Execute(tool);

        bool created = await vm.CommitRectToolAsync(0, new AnnotationRect(10, 20, 30, 40));

        created.Should().BeTrue();
        await service.Received(1).AddAsync(
            Arg.Any<string>(), Arg.Is<Annotation>(a => a.Kind == kind), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommitRectTool_Stamp_UsesStampLabel()
    {
        var service = EchoService();
        var vm = CreateVm(service);
        vm.SelectToolCommand.Execute(AnnotationTool.Stamp);
        vm.StampLabel = "REJECTED";

        bool created = await vm.CommitRectToolAsync(0, new AnnotationRect(0, 0, 100, 40));

        created.Should().BeTrue();
        await service.Received(1).AddAsync(
            Arg.Any<string>(), Arg.Is<Annotation>(a => a.Kind == AnnotationKind.Stamp && a.Text == "REJECTED"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommitRectTool_BlankStampLabel_NoOp()
    {
        var service = EchoService();
        var vm = CreateVm(service);
        vm.SelectToolCommand.Execute(AnnotationTool.Stamp);
        vm.StampLabel = "   ";

        bool created = await vm.CommitRectToolAsync(0, new AnnotationRect(0, 0, 100, 40));

        created.Should().BeFalse();
        await service.DidNotReceive().AddAsync(Arg.Any<string>(), Arg.Any<Annotation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommitRectTool_DegenerateRect_NoOp()
    {
        var service = EchoService();
        var vm = CreateVm(service);
        vm.SelectToolCommand.Execute(AnnotationTool.Rectangle);

        bool created = await vm.CommitRectToolAsync(0, new AnnotationRect(10, 10, 0, 50));

        created.Should().BeFalse();
        await service.DidNotReceive().AddAsync(Arg.Any<string>(), Arg.Any<Annotation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommitRectTool_WrongGesture_NoOp()
    {
        var vm = CreateVm();
        vm.SelectToolCommand.Execute(AnnotationTool.Line); // two-point, not rect

        bool created = await vm.CommitRectToolAsync(0, new AnnotationRect(0, 0, 10, 10));

        created.Should().BeFalse();
    }

    [Theory]
    [InlineData(AnnotationTool.Line, AnnotationKind.Line)]
    [InlineData(AnnotationTool.Arrow, AnnotationKind.Arrow)]
    public async Task CommitTwoPointTool_CreatesLineOrArrow(AnnotationTool tool, AnnotationKind kind)
    {
        var service = EchoService();
        var vm = CreateVm(service);
        vm.SelectToolCommand.Execute(tool);

        bool created = await vm.CommitTwoPointToolAsync(0, new AnnotationPoint(0, 0), new AnnotationPoint(50, 60));

        created.Should().BeTrue();
        await service.Received(1).AddAsync(
            Arg.Any<string>(), Arg.Is<Annotation>(a => a.Kind == kind), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommitTwoPointTool_SamePoint_NoOp()
    {
        var vm = CreateVm();
        vm.SelectToolCommand.Execute(AnnotationTool.Arrow);

        bool created = await vm.CommitTwoPointToolAsync(0, new AnnotationPoint(5, 5), new AnnotationPoint(5, 5));

        created.Should().BeFalse();
    }

    [Fact]
    public async Task CommitMultiPointTool_Freehand_RequiresTwoPoints()
    {
        var service = EchoService();
        var vm = CreateVm(service);
        vm.SelectToolCommand.Execute(AnnotationTool.Freehand);

        (await vm.CommitMultiPointToolAsync(0, [new AnnotationPoint(0, 0)])).Should().BeFalse();
        (await vm.CommitMultiPointToolAsync(0, [new AnnotationPoint(0, 0), new AnnotationPoint(1, 1)])).Should().BeTrue();
    }

    [Fact]
    public async Task CommitMultiPointTool_Polygon_RequiresThreePoints()
    {
        var service = EchoService();
        var vm = CreateVm(service);
        vm.SelectToolCommand.Execute(AnnotationTool.Polygon);

        (await vm.CommitMultiPointToolAsync(0, [new AnnotationPoint(0, 0), new AnnotationPoint(1, 1)])).Should().BeFalse();
        (await vm.CommitMultiPointToolAsync(
            0, [new AnnotationPoint(0, 0), new AnnotationPoint(10, 0), new AnnotationPoint(5, 10)])).Should().BeTrue();
    }

    [Fact]
    public async Task CommitPointTool_StickyNote_CreatesSquareAroundPoint()
    {
        var service = EchoService();
        var vm = CreateVm(service);
        vm.SelectToolCommand.Execute(AnnotationTool.StickyNote);
        vm.StickyNoteText = "hello";

        bool created = await vm.CommitPointToolAsync(0, new AnnotationPoint(100, 100));

        created.Should().BeTrue();
        await service.Received(1).AddAsync(
            Arg.Any<string>(),
            Arg.Is<Annotation>(a => a.Kind == AnnotationKind.StickyNote && a.Text == "hello"
                && a.Bounds!.Width == 18 && a.Bounds.Height == 18),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommitPointTool_WrongTool_NoOp()
    {
        var vm = CreateVm();
        vm.SelectToolCommand.Execute(AnnotationTool.Rectangle);

        (await vm.CommitPointToolAsync(0, new AnnotationPoint(0, 0))).Should().BeFalse();
    }

    // ───── B1d-multipage: on-page commit wrappers (page index from payload) ─────

    [Fact]
    public async Task CommitRectToolOnPageCommand_UsesPayloadPageIndex_NotCurrent()
    {
        var service = EchoService();
        var vm = CreateVm(service);
        vm.CurrentPageIndex = 0; // current is page 0, but payload targets page 2
        vm.SelectToolCommand.Execute(AnnotationTool.Rectangle);

        await vm.CommitRectToolOnPageCommand.ExecuteAsync(
            new RectOnPagePayload(2, new AnnotationRect(0, 0, 10, 10)));

        await service.Received(1).AddAsync(
            Arg.Any<string>(),
            Arg.Is<Annotation>(a => a.Kind == AnnotationKind.Rectangle && a.PageIndex == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommitPointToolOnPageCommand_UsesPayloadPageIndex_NotCurrent()
    {
        var service = EchoService();
        var vm = CreateVm(service);
        vm.CurrentPageIndex = 0;
        vm.SelectToolCommand.Execute(AnnotationTool.StickyNote);

        await vm.CommitPointToolOnPageCommand.ExecuteAsync(
            new PointOnPagePayload(2, new AnnotationPoint(50, 50)));

        await service.Received(1).AddAsync(
            Arg.Any<string>(),
            Arg.Is<Annotation>(a => a.Kind == AnnotationKind.StickyNote && a.PageIndex == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommitTwoPointToolOnPageCommand_UsesPayloadPageIndex_NotCurrent()
    {
        var service = EchoService();
        var vm = CreateVm(service);
        vm.CurrentPageIndex = 0;
        vm.SelectToolCommand.Execute(AnnotationTool.Arrow);

        await vm.CommitTwoPointToolOnPageCommand.ExecuteAsync(
            new TwoPointOnPagePayload(2, new AnnotationPoint(0, 0), new AnnotationPoint(40, 50)));

        await service.Received(1).AddAsync(
            Arg.Any<string>(),
            Arg.Is<Annotation>(a => a.Kind == AnnotationKind.Arrow && a.PageIndex == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommitMultiPointToolOnPageCommand_Freehand_UsesPayloadPageIndex_NotCurrent()
    {
        var service = EchoService();
        var vm = CreateVm(service);
        vm.CurrentPageIndex = 0;
        vm.SelectToolCommand.Execute(AnnotationTool.Freehand);

        await vm.CommitMultiPointToolOnPageCommand.ExecuteAsync(
            new MultiPointOnPagePayload(2, [new(0, 0), new(5, 5), new(10, 2)]));

        await service.Received(1).AddAsync(
            Arg.Any<string>(),
            Arg.Is<Annotation>(a => a.Kind == AnnotationKind.Freehand && a.PageIndex == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommitRectToolOnPageCommand_NullPayload_NoOp()
    {
        var service = EchoService();
        var vm = CreateVm(service);
        vm.SelectToolCommand.Execute(AnnotationTool.Rectangle);

        await vm.CommitRectToolOnPageCommand.ExecuteAsync(null);

        await service.DidNotReceive().AddAsync(Arg.Any<string>(), Arg.Any<Annotation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommitMultiPointToolOnPageCommand_NullPayload_NoOp()
    {
        var service = EchoService();
        var vm = CreateVm(service);
        vm.SelectToolCommand.Execute(AnnotationTool.Freehand);

        await vm.CommitMultiPointToolOnPageCommand.ExecuteAsync(null);

        await service.DidNotReceive().AddAsync(Arg.Any<string>(), Arg.Any<Annotation>(), Arg.Any<CancellationToken>());
    }

    // ───── B1d: color swatch + stamp label collection ─────

    [Fact]
    public void SetNewAnnotationColorCommand_UpdatesProperty()
    {
        var vm = CreateVm();
        string original = vm.NewAnnotationColorHex;

        vm.SetNewAnnotationColorCommand.Execute("#2196F3");

        vm.NewAnnotationColorHex.Should().Be("#2196F3").And.NotBe(original);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetNewAnnotationColorCommand_BlankInput_NoOp(string? hex)
    {
        var vm = CreateVm();
        string before = vm.NewAnnotationColorHex;

        vm.SetNewAnnotationColorCommand.Execute(hex);

        vm.NewAnnotationColorHex.Should().Be(before);
    }

    [Fact]
    public void StampLabels_HasDefaultPresets()
    {
        var vm = CreateVm();

        vm.StampLabels.Should().BeEquivalentTo(["APPROVED", "REJECTED", "DRAFT"]);
    }
}
