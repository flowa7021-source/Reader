using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelSanitizationTests
{
    private static readonly DocumentMetadata SampleMetadata = new(
        Title: "t", Author: "a", Subject: "s",
        Created: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Modified: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Custom: new Dictionary<string, string>());

    private static DocumentTabViewModel CreateVm(string filePath = "/tmp/x.pdf", IPdfSanitizationService? svc = null)
    {
        var document = Substitute.For<IDocument>();
        document.PageCount.Returns(3);
        document.Metadata.Returns(SampleMetadata);
        var search = Substitute.For<ISearchService>();
        search.SearchInDocumentAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<IReadOnlyList<SearchHit>>([]));
        var ann = Substitute.For<IAnnotationService>();
        ann.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Annotation>>([]));
        var bm = Substitute.For<IBookmarkService>();
        bm.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Bookmark>>([]));
        return new DocumentTabViewModel(document, filePath, search, ann, bm,
            NullLogger<DocumentTabViewModel>.Instance, sanitizationService: svc);
    }

    private static PdfSanitizationReport SampleReport() => new(["s1"], HasJavaScriptOpenAction: true, HasDocumentAdditionalActions: false);

    [Fact]
    public void CanSanitize_NoService_False() => CreateVm(svc: null).CanSanitize.Should().BeFalse();

    [Fact]
    public void CanSanitize_NonPdf_False() =>
        CreateVm(filePath: "/tmp/f.djvu", svc: Substitute.For<IPdfSanitizationService>()).CanSanitize.Should().BeFalse();

    [Fact]
    public void CanSanitize_PdfAndService_True() =>
        CreateVm(filePath: "/tmp/f.PDF", svc: Substitute.For<IPdfSanitizationService>()).CanSanitize.Should().BeTrue();

    [Fact]
    public async Task ScanForJavaScriptCommand_PopulatesReport()
    {
        var svc = Substitute.For<IPdfSanitizationService>();
        svc.ScanAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(SampleReport()));
        var vm = CreateVm(filePath: "/tmp/in.pdf", svc: svc);

        await vm.ScanForJavaScriptCommand.ExecuteAsync(null);

        vm.CurrentSanitizationReport.Should().NotBeNull();
        vm.CurrentSanitizationReport!.DocumentJavaScriptNames.Should().Equal("s1");
        vm.CurrentSanitizationReport.HasJavaScriptOpenAction.Should().BeTrue();
        vm.CurrentSanitizationReport.HasDocumentAdditionalActions.Should().BeFalse();
    }

    [Fact]
    public async Task ScanForJavaScriptCommand_ServiceThrows_DoesNotPropagate()
    {
        var svc = Substitute.For<IPdfSanitizationService>();
        svc.ScanAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException<PdfSanitizationReport>(new InvalidOperationException("boom")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", svc: svc);

        Func<Task> act = async () => await vm.ScanForJavaScriptCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RemoveJavaScriptCommand_ForwardsArgs()
    {
        var svc = Substitute.For<IPdfSanitizationService>();
        svc.RemoveJavaScriptAndActionsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult(true));
        var vm = CreateVm(filePath: "/tmp/in.pdf", svc: svc);

        await vm.RemoveJavaScriptCommand.ExecuteAsync(new RemoveJavaScriptRequest("/tmp/out.pdf"));

        await svc.Received(1).RemoveJavaScriptAndActionsAsync("/tmp/in.pdf", "/tmp/out.pdf", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void RemoveJavaScriptCommand_NonPdf_CannotExecute() =>
        CreateVm(filePath: "/tmp/in.png", svc: Substitute.For<IPdfSanitizationService>())
            .RemoveJavaScriptCommand.CanExecute(new RemoveJavaScriptRequest("/o.pdf")).Should().BeFalse();

    [Fact]
    public async Task RemoveJavaScriptCommand_BlankTarget_NoOp()
    {
        var svc = Substitute.For<IPdfSanitizationService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", svc: svc);

        await vm.RemoveJavaScriptCommand.ExecuteAsync(new RemoveJavaScriptRequest("  "));

        await svc.DidNotReceiveWithAnyArgs().RemoveJavaScriptAndActionsAsync(default!, default!, default);
    }

    [Fact]
    public async Task RemoveJavaScriptCommand_ServiceThrows_DoesNotPropagate()
    {
        var svc = Substitute.For<IPdfSanitizationService>();
        svc.RemoveJavaScriptAndActionsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException<bool>(new InvalidOperationException("boom")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", svc: svc);

        Func<Task> act = async () => await vm.RemoveJavaScriptCommand.ExecuteAsync(new RemoveJavaScriptRequest("/o.pdf"));

        await act.Should().NotThrowAsync();
    }
}
