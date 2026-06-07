using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

/// <summary>
/// Covers the custom <c>/Info</c> properties commands (<see cref="IPdfCustomPropertiesService"/>
/// wiring). Same gate / load-snapshot / arg-forwarding / no-op / suppress shape as
/// <see cref="DocumentTabViewModelPageLabelsTests"/>.
/// </summary>
public sealed class DocumentTabViewModelCustomPropertiesTests
{
    private static DocumentTabViewModel CreateVm(
        string filePath = "/tmp/x.pdf",
        IPdfCustomPropertiesService? customProps = null)
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
            customPropertiesService: customProps);
    }

    private static IReadOnlyList<PdfCustomProperty> Sample() =>
    [
        new PdfCustomProperty("Company", "Acme"),
        new PdfCustomProperty("Status", "Final"),
    ];

    // ───── CanEditCustomProperties gate ─────

    [Fact]
    public void CanEditCustomProperties_NoService_False()
    {
        var vm = CreateVm(customProps: null);
        vm.CanEditCustomProperties.Should().BeFalse();
    }

    [Fact]
    public void CanEditCustomProperties_NonPdfSource_False()
    {
        var vm = CreateVm(filePath: "/tmp/foo.epub", customProps: Substitute.For<IPdfCustomPropertiesService>());
        vm.CanEditCustomProperties.Should().BeFalse();
    }

    [Fact]
    public void CanEditCustomProperties_PdfSourceAndService_True()
    {
        var vm = CreateVm(filePath: "/tmp/foo.PDF", customProps: Substitute.For<IPdfCustomPropertiesService>());
        vm.CanEditCustomProperties.Should().BeTrue();
    }

    // ───── LoadCustomPropertiesCommand ─────

    [Fact]
    public async Task LoadCustomPropertiesCommand_OnPdf_LoadsIntoSnapshot()
    {
        var svc = Substitute.For<IPdfCustomPropertiesService>();
        svc.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult(Sample()));
        var vm = CreateVm(filePath: "/tmp/in.pdf", customProps: svc);

        await vm.LoadCustomPropertiesCommand.ExecuteAsync(null);

        vm.CurrentCustomProperties.Should().HaveCount(2);
        vm.CurrentCustomProperties[0].Name.Should().Be("Company");
        vm.CurrentCustomProperties[0].Value.Should().Be("Acme");
        vm.CurrentCustomProperties[1].Name.Should().Be("Status");
    }

    [Fact]
    public async Task LoadCustomPropertiesCommand_ReloadReplacesPreviousSnapshot()
    {
        var svc = Substitute.For<IPdfCustomPropertiesService>();
        svc.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(
               Task.FromResult<IReadOnlyList<PdfCustomProperty>>([new("A", "1"), new("B", "2")]),
               Task.FromResult<IReadOnlyList<PdfCustomProperty>>([new("Only", "x")]));
        var vm = CreateVm(filePath: "/tmp/in.pdf", customProps: svc);

        await vm.LoadCustomPropertiesCommand.ExecuteAsync(null);
        await vm.LoadCustomPropertiesCommand.ExecuteAsync(null);

        vm.CurrentCustomProperties.Should().HaveCount(1);
        vm.CurrentCustomProperties[0].Name.Should().Be("Only");
    }

    [Fact]
    public void LoadCustomPropertiesCommand_OnNonPdf_DoesNotExecute()
    {
        var svc = Substitute.For<IPdfCustomPropertiesService>();
        var vm = CreateVm(filePath: "/tmp/in.png", customProps: svc);

        vm.LoadCustomPropertiesCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task LoadCustomPropertiesCommand_ServiceThrows_DoesNotPropagate()
    {
        var svc = Substitute.For<IPdfCustomPropertiesService>();
        svc.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException<IReadOnlyList<PdfCustomProperty>>(new InvalidOperationException("boom")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", customProps: svc);

        Func<Task> act = async () => await vm.LoadCustomPropertiesCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync();
        vm.CurrentCustomProperties.Should().BeEmpty();
    }

    // ───── SaveCustomPropertiesCommand ─────

    [Fact]
    public async Task SaveCustomPropertiesCommand_ForwardsArgs_ToService()
    {
        var svc = Substitute.For<IPdfCustomPropertiesService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", customProps: svc);
        var props = Sample();

        await vm.SaveCustomPropertiesCommand.ExecuteAsync(
            new SaveCustomPropertiesRequest(props, "/tmp/out.pdf"));

        await svc.Received(1).SetAsync(
            "/tmp/in.pdf", "/tmp/out.pdf", props, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveCustomPropertiesCommand_NullRequest_NoOp()
    {
        var svc = Substitute.For<IPdfCustomPropertiesService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", customProps: svc);

        await vm.SaveCustomPropertiesCommand.ExecuteAsync(null);

        await svc.DidNotReceiveWithAnyArgs().SetAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task SaveCustomPropertiesCommand_BlankTargetPath_NoOp()
    {
        var svc = Substitute.For<IPdfCustomPropertiesService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", customProps: svc);

        await vm.SaveCustomPropertiesCommand.ExecuteAsync(
            new SaveCustomPropertiesRequest(Sample(), "   "));

        await svc.DidNotReceiveWithAnyArgs().SetAsync(default!, default!, default!, default);
    }

    [Fact]
    public void SaveCustomPropertiesCommand_OnNonPdf_DoesNotExecute()
    {
        var svc = Substitute.For<IPdfCustomPropertiesService>();
        var vm = CreateVm(filePath: "/tmp/in.png", customProps: svc);

        vm.SaveCustomPropertiesCommand.CanExecute(
            new SaveCustomPropertiesRequest(Sample(), "/tmp/out.pdf")).Should().BeFalse();
    }

    [Fact]
    public async Task SaveCustomPropertiesCommand_ServiceThrows_DoesNotPropagate()
    {
        var svc = Substitute.For<IPdfCustomPropertiesService>();
        svc.SetAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<PdfCustomProperty>>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException(new InvalidOperationException("boom")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", customProps: svc);

        Func<Task> act = async () => await vm.SaveCustomPropertiesCommand.ExecuteAsync(
            new SaveCustomPropertiesRequest(Sample(), "/tmp/out.pdf"));

        await act.Should().NotThrowAsync();
    }
}
