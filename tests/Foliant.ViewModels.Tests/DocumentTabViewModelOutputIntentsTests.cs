using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

/// <summary>
/// Covers the read-only output-intents listing command (<see cref="IPdfOutputIntentService"/> wiring).
/// Same gate / load-snapshot / suppress shape as the fonts/links partials.
/// </summary>
public sealed class DocumentTabViewModelOutputIntentsTests
{
    private static DocumentTabViewModel CreateVm(
        string filePath = "/tmp/x.pdf",
        IPdfOutputIntentService? service = null)
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
            outputIntentService: service);
    }

    private static IReadOnlyList<PdfOutputIntent> Sample() =>
    [
        new PdfOutputIntent("GTS_PDFX", "FOGRA39", "Coated FOGRA39", "http://www.color.org", null, HasIccProfile: true),
        new PdfOutputIntent("GTS_PDFA1", null, null, null, "sRGB", HasIccProfile: false),
    ];

    [Fact]
    public void CanListOutputIntents_NoService_False() =>
        CreateVm(service: null).CanListOutputIntents.Should().BeFalse();

    [Fact]
    public void CanListOutputIntents_NonPdfSource_False() =>
        CreateVm(filePath: "/tmp/foo.epub", service: Substitute.For<IPdfOutputIntentService>())
            .CanListOutputIntents.Should().BeFalse();

    [Fact]
    public void CanListOutputIntents_PdfSourceAndService_True() =>
        CreateVm(filePath: "/tmp/foo.PDF", service: Substitute.For<IPdfOutputIntentService>())
            .CanListOutputIntents.Should().BeTrue();

    [Fact]
    public async Task LoadOutputIntentsCommand_OnPdf_LoadsIntoSnapshot()
    {
        var svc = Substitute.For<IPdfOutputIntentService>();
        svc.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(Sample()));
        var vm = CreateVm(filePath: "/tmp/in.pdf", service: svc);

        await vm.LoadOutputIntentsCommand.ExecuteAsync(null);

        vm.CurrentOutputIntents.Should().HaveCount(2);
        vm.CurrentOutputIntents[0].Subtype.Should().Be("GTS_PDFX");
        vm.CurrentOutputIntents[0].HasIccProfile.Should().BeTrue();
        vm.CurrentOutputIntents[1].HasIccProfile.Should().BeFalse();
    }

    [Fact]
    public async Task LoadOutputIntentsCommand_ReloadReplacesSnapshot()
    {
        var svc = Substitute.For<IPdfOutputIntentService>();
        svc.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(
               Task.FromResult(Sample()),
               Task.FromResult<IReadOnlyList<PdfOutputIntent>>([new("GTS_PDFX", null, null, null, null, false)]));
        var vm = CreateVm(filePath: "/tmp/in.pdf", service: svc);

        await vm.LoadOutputIntentsCommand.ExecuteAsync(null);
        await vm.LoadOutputIntentsCommand.ExecuteAsync(null);

        vm.CurrentOutputIntents.Should().ContainSingle();
    }

    [Fact]
    public void LoadOutputIntentsCommand_OnNonPdf_DoesNotExecute() =>
        CreateVm(filePath: "/tmp/in.png", service: Substitute.For<IPdfOutputIntentService>())
            .LoadOutputIntentsCommand.CanExecute(null).Should().BeFalse();

    [Fact]
    public async Task LoadOutputIntentsCommand_ServiceThrows_DoesNotPropagate()
    {
        var svc = Substitute.For<IPdfOutputIntentService>();
        svc.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException<IReadOnlyList<PdfOutputIntent>>(new InvalidOperationException("boom")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", service: svc);

        Func<Task> act = async () => await vm.LoadOutputIntentsCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync();
        vm.CurrentOutputIntents.Should().BeEmpty();
    }
}
