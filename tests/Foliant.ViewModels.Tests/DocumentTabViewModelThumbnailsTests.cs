using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelThumbnailsTests
{
    private static DocumentTabViewModel CreateVm(int pageCount = 10)
    {
        var document = Substitute.For<IDocument>();
        document.PageCount.Returns(pageCount);
        document.Metadata.Returns(new DocumentMetadata(null, null, null, null, null, new Dictionary<string, string>()));

        var search = Substitute.For<ISearchService>();
        search.SearchInDocumentAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<IReadOnlyList<SearchHit>>([]));
        var annotations = Substitute.For<IAnnotationService>();
        annotations.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(Task.FromResult<IReadOnlyList<Annotation>>([]));
        var bookmarks = Substitute.For<IBookmarkService>();
        bookmarks.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([]));

        return new DocumentTabViewModel(document, "/tmp/x.pdf", search, annotations, bookmarks, NullLogger<DocumentTabViewModel>.Instance);
    }

    [Fact]
    public void Thumbnails_BuiltForEveryPage()
    {
        var vm = CreateVm(pageCount: 7);
        vm.Thumbnails.Pages.Should().HaveCount(7);
    }

    [Fact]
    public void ChangingCurrentPage_SelectsMatchingThumbnail()
    {
        var vm = CreateVm();
        vm.CurrentPageIndex = 4;

        vm.Thumbnails.SelectedPageIndex.Should().Be(4);
        vm.Thumbnails.Pages[4].IsSelected.Should().BeTrue();
    }

    [Fact]
    public void SelectingThumbnail_NavigatesToThatPage()
    {
        var vm = CreateVm();
        vm.Thumbnails.SelectedPageIndex = 2;

        vm.CurrentPageIndex.Should().Be(2);
    }
}
