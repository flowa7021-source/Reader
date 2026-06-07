using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

/// <summary>
/// Covers the OCG layer rename/delete commands (<see cref="IPdfOcgEditService"/> wiring) added on top
/// of the read + visibility-toggle layers feature. Same arg-forwarding / no-op / suppress shape as
/// <see cref="DocumentTabViewModelLayersTests"/>.
/// </summary>
public sealed class DocumentTabViewModelOcgEditTests
{
    private static DocumentTabViewModel CreateVm(
        string filePath = "/tmp/x.pdf",
        IPdfOcgEditService? ocgEdit = null)
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
            ocgEditService: ocgEdit);
    }

    // ───── CanEditLayers gate ─────

    [Fact]
    public void CanEditLayers_NoService_False()
    {
        var vm = CreateVm(ocgEdit: null);
        vm.CanEditLayers.Should().BeFalse();
    }

    [Fact]
    public void CanEditLayers_NonPdfSource_False()
    {
        var vm = CreateVm(filePath: "/tmp/foo.djvu", ocgEdit: Substitute.For<IPdfOcgEditService>());
        vm.CanEditLayers.Should().BeFalse();
    }

    [Fact]
    public void CanEditLayers_PdfSourceAndService_True()
    {
        var vm = CreateVm(filePath: "/tmp/foo.PDF", ocgEdit: Substitute.For<IPdfOcgEditService>());
        vm.CanEditLayers.Should().BeTrue();
    }

    // ───── RenameLayerCommand ─────

    [Fact]
    public async Task RenameLayerCommand_ForwardsArgs_ToService()
    {
        var svc = Substitute.For<IPdfOcgEditService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", ocgEdit: svc);

        await vm.RenameLayerCommand.ExecuteAsync(new RenameLayerRequest(2, "New name", "/tmp/out.pdf"));

        await svc.Received(1).RenameAsync(
            "/tmp/in.pdf", "/tmp/out.pdf", 2, "New name", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RenameLayerCommand_NullRequest_NoOp()
    {
        var svc = Substitute.For<IPdfOcgEditService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", ocgEdit: svc);

        await vm.RenameLayerCommand.ExecuteAsync(null);

        await svc.DidNotReceiveWithAnyArgs().RenameAsync(default!, default!, default, default!, default);
    }

    [Fact]
    public async Task RenameLayerCommand_BlankTargetPath_NoOp()
    {
        var svc = Substitute.For<IPdfOcgEditService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", ocgEdit: svc);

        await vm.RenameLayerCommand.ExecuteAsync(new RenameLayerRequest(0, "X", "   "));

        await svc.DidNotReceiveWithAnyArgs().RenameAsync(default!, default!, default, default!, default);
    }

    [Fact]
    public async Task RenameLayerCommand_BlankNewName_NoOp()
    {
        var svc = Substitute.For<IPdfOcgEditService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", ocgEdit: svc);

        await vm.RenameLayerCommand.ExecuteAsync(new RenameLayerRequest(0, "  ", "/tmp/out.pdf"));

        await svc.DidNotReceiveWithAnyArgs().RenameAsync(default!, default!, default, default!, default);
    }

    [Fact]
    public void RenameLayerCommand_OnNonPdf_DoesNotExecute()
    {
        var svc = Substitute.For<IPdfOcgEditService>();
        var vm = CreateVm(filePath: "/tmp/in.png", ocgEdit: svc);

        vm.RenameLayerCommand.CanExecute(new RenameLayerRequest(0, "X", "/tmp/out.pdf")).Should().BeFalse();
    }

    [Fact]
    public async Task RenameLayerCommand_ServiceThrows_DoesNotPropagate()
    {
        var svc = Substitute.For<IPdfOcgEditService>();
        svc.RenameAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException(new InvalidOperationException("boom")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", ocgEdit: svc);

        Func<Task> act = async () => await vm.RenameLayerCommand.ExecuteAsync(
            new RenameLayerRequest(0, "X", "/tmp/out.pdf"));

        await act.Should().NotThrowAsync();
    }

    // ───── DeleteLayerCommand ─────

    [Fact]
    public async Task DeleteLayerCommand_ForwardsArgs_ToService()
    {
        var svc = Substitute.For<IPdfOcgEditService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", ocgEdit: svc);

        await vm.DeleteLayerCommand.ExecuteAsync(new DeleteLayerRequest(1, "/tmp/out.pdf"));

        await svc.Received(1).RemoveAsync(
            "/tmp/in.pdf", "/tmp/out.pdf", 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteLayerCommand_NullRequest_NoOp()
    {
        var svc = Substitute.For<IPdfOcgEditService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", ocgEdit: svc);

        await vm.DeleteLayerCommand.ExecuteAsync(null);

        await svc.DidNotReceiveWithAnyArgs().RemoveAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task DeleteLayerCommand_BlankTargetPath_NoOp()
    {
        var svc = Substitute.For<IPdfOcgEditService>();
        var vm = CreateVm(filePath: "/tmp/in.pdf", ocgEdit: svc);

        await vm.DeleteLayerCommand.ExecuteAsync(new DeleteLayerRequest(0, "   "));

        await svc.DidNotReceiveWithAnyArgs().RemoveAsync(default!, default!, default, default);
    }

    [Fact]
    public void DeleteLayerCommand_OnNonPdf_DoesNotExecute()
    {
        var svc = Substitute.For<IPdfOcgEditService>();
        var vm = CreateVm(filePath: "/tmp/in.png", ocgEdit: svc);

        vm.DeleteLayerCommand.CanExecute(new DeleteLayerRequest(0, "/tmp/out.pdf")).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteLayerCommand_ServiceThrows_DoesNotPropagate()
    {
        var svc = Substitute.For<IPdfOcgEditService>();
        svc.RemoveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException(new InvalidOperationException("boom")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", ocgEdit: svc);

        Func<Task> act = async () => await vm.DeleteLayerCommand.ExecuteAsync(
            new DeleteLayerRequest(0, "/tmp/out.pdf"));

        await act.Should().NotThrowAsync();
    }
}
