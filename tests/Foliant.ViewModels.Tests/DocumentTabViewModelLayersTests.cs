using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelLayersTests
{
    private static DocumentTabViewModel CreateVm(
        string filePath = "/tmp/x.pdf",
        IPdfOcgService? ocg = null)
    {
        var document = Substitute.For<IDocument>();
        document.PageCount.Returns(3);
        document.Metadata.Returns(new DocumentMetadata(null, null, null, null, null, new Dictionary<string, string>()));

        var search = Substitute.For<ISearchService>();
        search.SearchInDocumentAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<IReadOnlyList<SearchHit>>([]));
        var ann = Substitute.For<IAnnotationService>();
        ann.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Annotation>>([]));
        var bm = Substitute.For<IBookmarkService>();
        bm.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([]));

        return new DocumentTabViewModel(
            document, filePath, search, ann, bm,
            NullLogger<DocumentTabViewModel>.Instance,
            ocgService: ocg);
    }

    private static IReadOnlyList<PdfLayer> SampleLayers() =>
    [
        new PdfLayer(0, "Background", IsVisible: true),
        new PdfLayer(1, "Annotations", IsVisible: true),
        new PdfLayer(2, "Watermark", IsVisible: false),
    ];

    // ───── CanShowLayers gate ─────

    [Fact]
    public void CanShowLayers_NoService_False()
    {
        var vm = CreateVm(ocg: null);
        vm.CanShowLayers.Should().BeFalse();
    }

    [Fact]
    public void CanShowLayers_NonPdfSource_False()
    {
        var vm = CreateVm(filePath: "/tmp/foo.djvu", ocg: Substitute.For<IPdfOcgService>());
        vm.CanShowLayers.Should().BeFalse();
    }

    [Fact]
    public void CanShowLayers_PdfSourceAndService_True()
    {
        var vm = CreateVm(filePath: "/tmp/foo.PDF", ocg: Substitute.For<IPdfOcgService>());
        vm.CanShowLayers.Should().BeTrue();
    }

    // ───── ShowLayersCommand ─────

    [Fact]
    public async Task ShowLayersCommand_OnPdf_LoadsLayersIntoCollection()
    {
        var svc = Substitute.For<IPdfOcgService>();
        var layers = SampleLayers();
        svc.ReadLayersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult(layers));
        var vm = CreateVm(filePath: "/tmp/in.pdf", ocg: svc);

        await vm.ShowLayersCommand.ExecuteAsync(null);

        vm.CurrentLayers.Should().HaveCount(3);
        vm.CurrentLayers[0].Index.Should().Be(0);
        vm.CurrentLayers[0].LayerName.Should().Be("Background");
        vm.CurrentLayers[0].IsVisible.Should().BeTrue();
        vm.CurrentLayers[2].LayerName.Should().Be("Watermark");
        vm.CurrentLayers[2].IsVisible.Should().BeFalse();
    }

    [Fact]
    public async Task ShowLayersCommand_ReloadReplacesPreviousSnapshot()
    {
        var svc = Substitute.For<IPdfOcgService>();
        svc.ReadLayersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(
               Task.FromResult<IReadOnlyList<PdfLayer>>([new(0, "A", true), new(1, "B", false)]),
               Task.FromResult<IReadOnlyList<PdfLayer>>([new(0, "Only", true)]));
        var vm = CreateVm(filePath: "/tmp/in.pdf", ocg: svc);

        await vm.ShowLayersCommand.ExecuteAsync(null);
        await vm.ShowLayersCommand.ExecuteAsync(null);

        vm.CurrentLayers.Should().HaveCount(1);
        vm.CurrentLayers[0].LayerName.Should().Be("Only");
    }

    [Fact]
    public void ShowLayersCommand_OnNonPdf_DoesNotExecute()
    {
        var svc = Substitute.For<IPdfOcgService>();
        var vm = CreateVm(filePath: "/tmp/in.png", ocg: svc);

        vm.ShowLayersCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void ShowLayersCommand_ServiceNull_DoesNotExecute()
    {
        var vm = CreateVm(filePath: "/tmp/in.pdf", ocg: null);

        vm.CanShowLayers.Should().BeFalse();
        vm.ShowLayersCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task ShowLayersCommand_ServiceThrows_DoesNotPropagate()
    {
        var svc = Substitute.For<IPdfOcgService>();
        svc.ReadLayersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException<IReadOnlyList<PdfLayer>>(new InvalidOperationException("boom")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", ocg: svc);

        Func<Task> act = async () => await vm.ShowLayersCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync();
        vm.CurrentLayers.Should().BeEmpty();
    }

    // ───── SaveLayerVisibilityCommand ─────

    [Fact]
    public async Task SaveLayerVisibilityCommand_ForwardsArgs_ToService()
    {
        var svc = Substitute.For<IPdfOcgService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", ocg: svc);

        var map = new Dictionary<int, bool> { [0] = false, [2] = true };
        await vm.SaveLayerVisibilityCommand.ExecuteAsync(
            new SaveLayerVisibilityRequest(map, "/tmp/out.pdf"));

        await svc.Received(1).SetLayerVisibilityAsync(
            "/tmp/in.pdf", "/tmp/out.pdf", map, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveLayerVisibilityCommand_NullRequest_NoOp()
    {
        var svc = Substitute.For<IPdfOcgService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", ocg: svc);

        await vm.SaveLayerVisibilityCommand.ExecuteAsync(null);

        await svc.DidNotReceiveWithAnyArgs().SetLayerVisibilityAsync(
            default!, default!, default!, default);
    }

    [Fact]
    public async Task SaveLayerVisibilityCommand_BlankTargetPath_NoOp()
    {
        var svc = Substitute.For<IPdfOcgService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", ocg: svc);

        await vm.SaveLayerVisibilityCommand.ExecuteAsync(
            new SaveLayerVisibilityRequest(new Dictionary<int, bool> { [0] = false }, "   "));

        await svc.DidNotReceiveWithAnyArgs().SetLayerVisibilityAsync(
            default!, default!, default!, default);
    }

    [Fact]
    public void SaveLayerVisibilityCommand_OnNonPdf_DoesNotExecute()
    {
        var svc = Substitute.For<IPdfOcgService>();
        var vm = CreateVm(filePath: "/tmp/in.png", ocg: svc);

        vm.SaveLayerVisibilityCommand.CanExecute(
            new SaveLayerVisibilityRequest(new Dictionary<int, bool> { [0] = false }, "/tmp/out.pdf"))
          .Should().BeFalse();
    }

    [Fact]
    public async Task SaveLayerVisibilityCommand_ServiceThrows_DoesNotPropagate()
    {
        var svc = Substitute.For<IPdfOcgService>();
        svc.SetLayerVisibilityAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<int, bool>>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException(new InvalidOperationException("boom")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", ocg: svc);

        Func<Task> act = async () => await vm.SaveLayerVisibilityCommand.ExecuteAsync(
            new SaveLayerVisibilityRequest(new Dictionary<int, bool> { [0] = false }, "/tmp/out.pdf"));

        await act.Should().NotThrowAsync();
    }

    // ───── PdfLayerViewModel ─────

    [Fact]
    public void PdfLayerViewModel_FromRecord_CopiesAllProperties()
    {
        var source = new PdfLayer(7, "Sample", IsVisible: true);

        var vm = new PdfLayerViewModel(source);

        vm.Index.Should().Be(7);
        vm.LayerName.Should().Be("Sample");
        vm.IsVisible.Should().BeTrue();
    }

    [Fact]
    public void PdfLayerViewModel_ToggleIsVisible_RaisesPropertyChanged()
    {
        var vm = new PdfLayerViewModel(new PdfLayer(0, "X", IsVisible: false));
        var received = new List<string?>();
        vm.PropertyChanged += (_, e) => received.Add(e.PropertyName);

        vm.IsVisible = true;

        received.Should().Contain(nameof(PdfLayerViewModel.IsVisible));
        vm.IsVisible.Should().BeTrue();
    }
}
