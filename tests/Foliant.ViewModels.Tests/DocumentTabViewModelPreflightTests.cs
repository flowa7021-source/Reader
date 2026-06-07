using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

/// <summary>
/// Covers the read-only preflight command (<see cref="IPdfPreflightService"/> wiring). Same gate /
/// suppress shape as the fonts/output-intents partials, but the snapshot is a single report object.
/// </summary>
public sealed class DocumentTabViewModelPreflightTests
{
    private static DocumentTabViewModel CreateVm(
        string filePath = "/tmp/x.pdf",
        IPdfPreflightService? service = null)
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
            preflightService: service);
    }

    private static PdfPreflightReport Sample() =>
        new(PageCount: 10, PdfVersion: "1.7", IsEncrypted: false, FontCount: 4, NonEmbeddedFontCount: 1,
            HasJavaScriptOrActions: false, OutputIntentCount: 1, HasIccOutputIntent: true, LinkCount: 3,
            HasExtractableText: true);

    [Fact]
    public void CanPreflight_NoService_False() =>
        CreateVm(service: null).CanPreflight.Should().BeFalse();

    [Fact]
    public void CanPreflight_NonPdfSource_False() =>
        CreateVm(filePath: "/tmp/foo.epub", service: Substitute.For<IPdfPreflightService>())
            .CanPreflight.Should().BeFalse();

    [Fact]
    public void CanPreflight_PdfSourceAndService_True() =>
        CreateVm(filePath: "/tmp/foo.PDF", service: Substitute.For<IPdfPreflightService>())
            .CanPreflight.Should().BeTrue();

    [Fact]
    public async Task LoadPreflightCommand_OnPdf_LoadsReport()
    {
        var svc = Substitute.For<IPdfPreflightService>();
        svc.PreflightAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(Sample()));
        var vm = CreateVm(filePath: "/tmp/in.pdf", service: svc);

        await vm.LoadPreflightCommand.ExecuteAsync(null);

        vm.CurrentPreflightReport.Should().NotBeNull();
        vm.CurrentPreflightReport!.PageCount.Should().Be(10);
        vm.CurrentPreflightReport.NonEmbeddedFontCount.Should().Be(1);
        vm.CurrentPreflightReport.HasIccOutputIntent.Should().BeTrue();
    }

    [Fact]
    public void LoadPreflightCommand_OnNonPdf_DoesNotExecute() =>
        CreateVm(filePath: "/tmp/in.png", service: Substitute.For<IPdfPreflightService>())
            .LoadPreflightCommand.CanExecute(null).Should().BeFalse();

    [Fact]
    public async Task LoadPreflightCommand_ServiceThrows_DoesNotPropagate_ReportNull()
    {
        var svc = Substitute.For<IPdfPreflightService>();
        svc.PreflightAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException<PdfPreflightReport>(new InvalidOperationException("boom")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", service: svc);

        Func<Task> act = async () => await vm.LoadPreflightCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync();
        vm.CurrentPreflightReport.Should().BeNull();
    }
}
