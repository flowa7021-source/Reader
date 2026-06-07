using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

/// <summary>
/// Covers the read-only rich-outline inspection command (<see cref="IPdfOutlineInspector"/> wiring).
/// Same gate / load-snapshot / suppress shape as the fonts/links partials.
/// </summary>
public sealed class DocumentTabViewModelOutlineViewTests
{
    private static DocumentTabViewModel CreateVm(
        string filePath = "/tmp/x.pdf",
        IPdfOutlineInspector? inspector = null)
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
            outlineInspector: inspector);
    }

    private static IReadOnlyList<DocumentOutlineEntry> Sample() =>
    [
        new DocumentOutlineEntry(0, "Cover", 0, OutlineDestinationMode.FitPage, IsBold: true),
        new DocumentOutlineEntry(2, "Chapter 1", 0, OutlineDestinationMode.FitWidth, IsOpen: false),
        new DocumentOutlineEntry(3, "Section 1.1", 1, OutlineDestinationMode.InheritZoom, Color: new OutlineColor(1, 0, 0)),
    ];

    [Fact]
    public void CanViewOutline_NoService_False() =>
        CreateVm(inspector: null).CanViewOutline.Should().BeFalse();

    [Fact]
    public void CanViewOutline_NonPdfSource_False() =>
        CreateVm(filePath: "/tmp/foo.epub", inspector: Substitute.For<IPdfOutlineInspector>())
            .CanViewOutline.Should().BeFalse();

    [Fact]
    public void CanViewOutline_PdfSourceAndService_True() =>
        CreateVm(filePath: "/tmp/foo.PDF", inspector: Substitute.For<IPdfOutlineInspector>())
            .CanViewOutline.Should().BeTrue();

    [Fact]
    public async Task LoadOutlineCommand_OnPdf_LoadsRichSnapshot()
    {
        var svc = Substitute.For<IPdfOutlineInspector>();
        svc.ReadRichAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(Sample()));
        var vm = CreateVm(filePath: "/tmp/in.pdf", inspector: svc);

        await vm.LoadOutlineCommand.ExecuteAsync(null);

        vm.CurrentOutline.Should().HaveCount(3);
        vm.CurrentOutline[0].IsBold.Should().BeTrue();
        vm.CurrentOutline[1].Destination.Should().Be(OutlineDestinationMode.FitWidth);
        vm.CurrentOutline[1].IsOpen.Should().BeFalse();
        vm.CurrentOutline[2].Depth.Should().Be(1);
        vm.CurrentOutline[2].Color.Should().Be(new OutlineColor(1, 0, 0));
    }

    [Fact]
    public async Task LoadOutlineCommand_ReloadReplacesSnapshot()
    {
        var svc = Substitute.For<IPdfOutlineInspector>();
        svc.ReadRichAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(
               Task.FromResult(Sample()),
               Task.FromResult<IReadOnlyList<DocumentOutlineEntry>>([new(0, "Only", 0)]));
        var vm = CreateVm(filePath: "/tmp/in.pdf", inspector: svc);

        await vm.LoadOutlineCommand.ExecuteAsync(null);
        await vm.LoadOutlineCommand.ExecuteAsync(null);

        vm.CurrentOutline.Should().ContainSingle();
        vm.CurrentOutline[0].Title.Should().Be("Only");
    }

    [Fact]
    public void LoadOutlineCommand_OnNonPdf_DoesNotExecute() =>
        CreateVm(filePath: "/tmp/in.png", inspector: Substitute.For<IPdfOutlineInspector>())
            .LoadOutlineCommand.CanExecute(null).Should().BeFalse();

    [Fact]
    public async Task LoadOutlineCommand_ServiceThrows_DoesNotPropagate()
    {
        var svc = Substitute.For<IPdfOutlineInspector>();
        svc.ReadRichAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException<IReadOnlyList<DocumentOutlineEntry>>(new InvalidOperationException("boom")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", inspector: svc);

        Func<Task> act = async () => await vm.LoadOutlineCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync();
        vm.CurrentOutline.Should().BeEmpty();
    }
}
