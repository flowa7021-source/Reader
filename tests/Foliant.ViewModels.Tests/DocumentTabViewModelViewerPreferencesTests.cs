using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelViewerPreferencesTests
{
    private static readonly DocumentMetadata SampleMetadata = new(
        Title: "t", Author: "a", Subject: "s",
        Created: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Modified: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Custom: new Dictionary<string, string>());

    private static DocumentTabViewModel CreateVm(
        string filePath = "/tmp/x.pdf",
        IPdfViewerPreferencesService? viewerPrefs = null)
    {
        var document = Substitute.For<IDocument>();
        document.PageCount.Returns(3);
        document.Metadata.Returns(SampleMetadata);

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
            viewerPreferencesService: viewerPrefs);
    }

    private static PdfViewerPreferences SamplePrefs() =>
        new(PdfPageLayout.TwoPageLeft, PdfPageMode.UseOutlines, true, false, true, false, true);

    // ───── CanEditViewerPreferences gate ─────

    [Fact]
    public void CanEditViewerPreferences_NoService_False() =>
        CreateVm(viewerPrefs: null).CanEditViewerPreferences.Should().BeFalse();

    [Fact]
    public void CanEditViewerPreferences_NonPdfSource_False() =>
        CreateVm(filePath: "/tmp/foo.epub", viewerPrefs: Substitute.For<IPdfViewerPreferencesService>())
            .CanEditViewerPreferences.Should().BeFalse();

    [Fact]
    public void CanEditViewerPreferences_PdfSourceAndService_True() =>
        CreateVm(filePath: "/tmp/foo.PDF", viewerPrefs: Substitute.For<IPdfViewerPreferencesService>())
            .CanEditViewerPreferences.Should().BeTrue();

    // ───── LoadViewerPreferencesCommand ─────

    [Fact]
    public async Task LoadViewerPreferencesCommand_PopulatesSnapshotFromService()
    {
        var svc = Substitute.For<IPdfViewerPreferencesService>();
        svc.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(SamplePrefs()));
        var vm = CreateVm(filePath: "/tmp/in.pdf", viewerPrefs: svc);

        await vm.LoadViewerPreferencesCommand.ExecuteAsync(null);

        vm.CurrentViewerPreferences.Should().Be(SamplePrefs());
    }

    [Fact]
    public async Task LoadViewerPreferencesCommand_ServiceThrows_KeepsDefault()
    {
        var svc = Substitute.For<IPdfViewerPreferencesService>();
        svc.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException<PdfViewerPreferences>(new InvalidOperationException("boom")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", viewerPrefs: svc);

        Func<Task> act = async () => await vm.LoadViewerPreferencesCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync();
        vm.CurrentViewerPreferences.Should().Be(PdfViewerPreferences.Default);
    }

    // ───── SaveViewerPreferencesCommand ─────

    [Fact]
    public async Task SaveViewerPreferencesCommand_ForwardsPrefsAndPath_ToService()
    {
        var svc = Substitute.For<IPdfViewerPreferencesService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", viewerPrefs: svc);
        var prefs = SamplePrefs();

        await vm.SaveViewerPreferencesCommand.ExecuteAsync(new SaveViewerPreferencesRequest(prefs, "/tmp/out.pdf"));

        await svc.Received(1).WriteAsync("/tmp/in.pdf", "/tmp/out.pdf", prefs, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SaveViewerPreferencesCommand_OnNonPdf_CannotExecute() =>
        CreateVm(filePath: "/tmp/in.png", viewerPrefs: Substitute.For<IPdfViewerPreferencesService>())
            .SaveViewerPreferencesCommand.CanExecute(new SaveViewerPreferencesRequest(SamplePrefs(), "/tmp/out.pdf"))
            .Should().BeFalse();

    [Fact]
    public void SaveViewerPreferencesCommand_ServiceNull_CannotExecute() =>
        CreateVm(filePath: "/tmp/in.pdf", viewerPrefs: null)
            .SaveViewerPreferencesCommand.CanExecute(new SaveViewerPreferencesRequest(SamplePrefs(), "/tmp/out.pdf"))
            .Should().BeFalse();

    [Fact]
    public async Task SaveViewerPreferencesCommand_NullRequest_NoOp()
    {
        var svc = Substitute.For<IPdfViewerPreferencesService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", viewerPrefs: svc);

        await vm.SaveViewerPreferencesCommand.ExecuteAsync(null);

        await svc.DidNotReceiveWithAnyArgs().WriteAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task SaveViewerPreferencesCommand_BlankTarget_NoOp()
    {
        var svc = Substitute.For<IPdfViewerPreferencesService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", viewerPrefs: svc);

        await vm.SaveViewerPreferencesCommand.ExecuteAsync(new SaveViewerPreferencesRequest(SamplePrefs(), "  "));

        await svc.DidNotReceiveWithAnyArgs().WriteAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task SaveViewerPreferencesCommand_ServiceThrows_DoesNotPropagate()
    {
        var svc = Substitute.For<IPdfViewerPreferencesService>();
        svc.WriteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<PdfViewerPreferences>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException(new InvalidOperationException("boom")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", viewerPrefs: svc);

        Func<Task> act = async () =>
            await vm.SaveViewerPreferencesCommand.ExecuteAsync(new SaveViewerPreferencesRequest(SamplePrefs(), "/tmp/out.pdf"));

        await act.Should().NotThrowAsync();
    }
}
