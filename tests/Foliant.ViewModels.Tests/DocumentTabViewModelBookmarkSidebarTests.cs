using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelBookmarkSidebarTests
{
    private static DocumentTabViewModel CreateVm(IBookmarkService bookmarks, string filePath = "/tmp/doc.pdf")
    {
        var doc = Substitute.For<IDocument>();
        doc.PageCount.Returns(10);
        doc.Metadata.Returns(new DocumentMetadata(null, null, null, null, null, new Dictionary<string, string>()));

        return new DocumentTabViewModel(
            doc,
            filePath,
            Substitute.For<ISearchService>(),
            Substitute.For<IAnnotationService>(),
            bookmarks,
            NullLogger<DocumentTabViewModel>.Instance);
    }

    [Fact]
    public void IsBookmarksVisible_DefaultsToFalse_ToggleFlips()
    {
        var bookmarks = Substitute.For<IBookmarkService>();
        bookmarks.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([]));
        var vm = CreateVm(bookmarks);

        vm.IsBookmarksVisible.Should().BeFalse();

        vm.ToggleBookmarksCommand.Execute(null);
        vm.IsBookmarksVisible.Should().BeTrue();

        vm.ToggleBookmarksCommand.Execute(null);
        vm.IsBookmarksVisible.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveBookmark_CallsServiceAndRemovesFromObservable()
    {
        var existing = new Bookmark(Guid.NewGuid(), 3, "Chapter", DateTimeOffset.UnixEpoch);
        var bookmarks = Substitute.For<IBookmarkService>();
        bookmarks.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([existing]));
        bookmarks.RemoveAsync(Arg.Any<string>(), existing.Id, Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult(true));

        var vm = CreateVm(bookmarks);
        await vm.LoadBookmarksAsync(default);
        vm.Bookmarks.Should().ContainSingle();

        await vm.RemoveBookmarkCommand.ExecuteAsync(existing);

        await bookmarks.Received().RemoveAsync(Arg.Any<string>(), existing.Id, Arg.Any<CancellationToken>());
        vm.Bookmarks.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveBookmark_NullArg_IsNoOp()
    {
        var bookmarks = Substitute.For<IBookmarkService>();
        bookmarks.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([]));
        var vm = CreateVm(bookmarks);

        await vm.RemoveBookmarkCommand.ExecuteAsync(null);

        await bookmarks.DidNotReceive().RemoveAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveBookmark_ServiceReturnsFalse_LeavesCollectionUnchanged()
    {
        var existing = new Bookmark(Guid.NewGuid(), 3, "Chapter", DateTimeOffset.UnixEpoch);
        var bookmarks = Substitute.For<IBookmarkService>();
        bookmarks.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([existing]));
        bookmarks.RemoveAsync(Arg.Any<string>(), existing.Id, Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult(false));

        var vm = CreateVm(bookmarks);
        await vm.LoadBookmarksAsync(default);

        await vm.RemoveBookmarkCommand.ExecuteAsync(existing);

        vm.Bookmarks.Should().ContainSingle();
    }
}
