using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Application.Settings;
using Foliant.Application.UseCases;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class MainViewModelMergeTests
{
    private static MainViewModel CreateVm(IPdfMergeService? merge = null)
    {
        var useCase = new OpenDocumentUseCase([], NullLogger<OpenDocumentUseCase>.Instance);
        Func<IDocument, string, DocumentTabViewModel> factory = (_, _) => throw new NotSupportedException();

        var recents = Substitute.For<IRecentsService>();
        recents.GetAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());

        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(AppSettings.Default);

        var localization = Substitute.For<ILocalizationService>();
        var indexer = Substitute.For<IDocumentIndexer>();

        return new MainViewModel(useCase, factory, recents, settings, localization, indexer,
            NullLogger<MainViewModel>.Instance, mergeService: merge);
    }

    // ───── CanMergePdfs ─────

    [Fact]
    public void CanMergePdfs_NoService_False()
    {
        CreateVm(merge: null).CanMergePdfs.Should().BeFalse();
    }

    [Fact]
    public void CanMergePdfs_ServicePresent_True()
    {
        CreateVm(merge: Substitute.For<IPdfMergeService>()).CanMergePdfs.Should().BeTrue();
    }

    // ───── MergePdfsCommand ─────

    [Fact]
    public async Task MergePdfs_ForwardsSourcesAndTarget_ToService()
    {
        var svc = Substitute.For<IPdfMergeService>();
        var vm = CreateVm(merge: svc);
        string[] sources = ["/tmp/a.pdf", "/tmp/b.pdf", "/tmp/c.pdf"];

        await vm.MergePdfsCommand.ExecuteAsync(new MergePdfsRequest(sources, "/tmp/out.pdf"));

        await svc.Received(1).MergeAsync(
            Arg.Is<IReadOnlyList<string>>(list => list.SequenceEqual(sources)),
            "/tmp/out.pdf",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MergePdfs_NullRequest_IsNoOp()
    {
        var svc = Substitute.For<IPdfMergeService>();
        var vm = CreateVm(merge: svc);

        await vm.MergePdfsCommand.ExecuteAsync(null);

        await svc.DidNotReceiveWithAnyArgs().MergeAsync(default!, default!, default);
    }

    [Fact]
    public async Task MergePdfs_BlankTargetPath_IsNoOp()
    {
        var svc = Substitute.For<IPdfMergeService>();
        var vm = CreateVm(merge: svc);

        await vm.MergePdfsCommand.ExecuteAsync(new MergePdfsRequest(["/tmp/a.pdf", "/tmp/b.pdf"], "   "));

        await svc.DidNotReceiveWithAnyArgs().MergeAsync(default!, default!, default);
    }

    [Fact]
    public async Task MergePdfs_FewerThanTwoSources_IsNoOp()
    {
        var svc = Substitute.For<IPdfMergeService>();
        var vm = CreateVm(merge: svc);

        await vm.MergePdfsCommand.ExecuteAsync(new MergePdfsRequest(["/tmp/a.pdf"], "/tmp/out.pdf"));

        await svc.DidNotReceiveWithAnyArgs().MergeAsync(default!, default!, default);
    }

    [Fact]
    public async Task MergePdfs_ServiceThrows_DoesNotPropagate()
    {
        var svc = Substitute.For<IPdfMergeService>();
        svc.MergeAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException(new InvalidOperationException("boom")));
        var vm = CreateVm(merge: svc);

        Func<Task> act = async () =>
            await vm.MergePdfsCommand.ExecuteAsync(new MergePdfsRequest(["/tmp/a.pdf", "/tmp/b.pdf"], "/tmp/out.pdf"));

        await act.Should().NotThrowAsync();
    }
}
