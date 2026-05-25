using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Application.UseCases;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelEditingTests
{
    [Fact]
    public async Task RotateCurrentPage_AppliesEdit_ThenReloadsDocument()
    {
        string path = Path.Combine(Path.GetTempPath(), $"foliant-edit-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(path, "%PDF-1.4 dummy");
        try
        {
            var initial = Substitute.For<IDocument>();
            initial.PageCount.Returns(3);

            var reopened = Substitute.For<IDocument>();
            reopened.PageCount.Returns(2);

            var loader = Substitute.For<IDocumentLoader>();
            loader.CanLoad(path).Returns(true);
            loader.LoadAsync(path, Arg.Any<CancellationToken>()).Returns(reopened);
            var openUseCase = new OpenDocumentUseCase([loader], NullLogger<OpenDocumentUseCase>.Instance);

            var pageEdit = Substitute.For<IPageEditService>();
            pageEdit.CanEdit(Arg.Any<IDocument>()).Returns(true);

            var vm = new DocumentTabViewModel(
                initial, path,
                Substitute.For<ISearchService>(),
                Substitute.For<IAnnotationService>(),
                Substitute.For<IBookmarkService>(),
                NullLogger<DocumentTabViewModel>.Instance,
                pageEdit: pageEdit,
                openUseCase: openUseCase);

            vm.CanEditPages.Should().BeTrue();

            await vm.RotateCurrentPageCommand.ExecuteAsync(null);

            await pageEdit.Received(1).RotatePageAsync(initial, 0, ViewRotation.Cw90, Arg.Any<CancellationToken>());
            await loader.Received(1).LoadAsync(path, Arg.Any<CancellationToken>());
            await initial.Received(1).DisposeAsync();
            vm.PageCount.Should().Be(2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CanEditPages_WhenNoPageEditService_IsFalse()
    {
        var doc = Substitute.For<IDocument>();
        doc.PageCount.Returns(1);
        var vm = new DocumentTabViewModel(
            doc, "/tmp/x.pdf",
            Substitute.For<ISearchService>(),
            Substitute.For<IAnnotationService>(),
            Substitute.For<IBookmarkService>(),
            NullLogger<DocumentTabViewModel>.Instance);

        vm.CanEditPages.Should().BeFalse();
        vm.CanDeleteCurrentPage.Should().BeFalse();
    }
}
