using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelOutlineImportTests
{
    private static DocumentTabViewModel CreateVm(
        IBookmarkService bookmarks,
        IPdfOutlineReader? outlineReader,
        string filePath = "/tmp/doc.pdf")
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
            NullLogger<DocumentTabViewModel>.Instance,
            pdfOutlineReader: outlineReader);
    }

    [Fact]
    public async Task ImportPdfOutline_AddsEachEntryViaService_AndReloads()
    {
        var bookmarks = Substitute.For<IBookmarkService>();
        bookmarks.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([]));
        bookmarks.AddAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(callInfo => Task.FromResult(new Bookmark(
                     Guid.NewGuid(), callInfo.ArgAt<int>(1), callInfo.ArgAt<string>(2), DateTimeOffset.UnixEpoch)));

        var outline = Substitute.For<IPdfOutlineReader>();
        outline.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<IReadOnlyList<DocumentOutlineEntry>>(
                   [
                       new DocumentOutlineEntry(0, "Cover", 0),
                       new DocumentOutlineEntry(4, "Chapter 1", 0),
                   ]));

        var vm = CreateVm(bookmarks, outline);

        await vm.ImportPdfOutlineCommand.ExecuteAsync(null);

        await bookmarks.Received().AddAsync(Arg.Any<string>(), 0, "Cover", Arg.Any<CancellationToken>());
        await bookmarks.Received().AddAsync(Arg.Any<string>(), 4, "Chapter 1", Arg.Any<CancellationToken>());
        // После импорта sidebar перезагружается через ListAsync (минимум один раз).
        await bookmarks.Received().ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportPdfOutline_EmptyOutline_NoOpsAgainstService()
    {
        var bookmarks = Substitute.For<IBookmarkService>();
        bookmarks.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([]));

        var outline = Substitute.For<IPdfOutlineReader>();
        outline.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<IReadOnlyList<DocumentOutlineEntry>>([]));

        var vm = CreateVm(bookmarks, outline);

        await vm.ImportPdfOutlineCommand.ExecuteAsync(null);

        await bookmarks.DidNotReceive().AddAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportPdfOutline_BlankTitle_FallsBackToPageLabel()
    {
        var bookmarks = Substitute.For<IBookmarkService>();
        bookmarks.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([]));
        bookmarks.AddAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(callInfo => Task.FromResult(new Bookmark(
                     Guid.NewGuid(), callInfo.ArgAt<int>(1), callInfo.ArgAt<string>(2), DateTimeOffset.UnixEpoch)));

        var outline = Substitute.For<IPdfOutlineReader>();
        outline.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(Task.FromResult<IReadOnlyList<DocumentOutlineEntry>>(
                   [new DocumentOutlineEntry(2, "   ", 0)]));

        var vm = CreateVm(bookmarks, outline);

        await vm.ImportPdfOutlineCommand.ExecuteAsync(null);

        // Whitespace title → "Page 3" (1-based, как везде в UI).
        await bookmarks.Received().AddAsync(Arg.Any<string>(), 2, "Page 3", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CanImportPdfOutline_FalseForNonPdf_FalseWithoutReader_TrueForPdf()
    {
        var bookmarks = Substitute.For<IBookmarkService>();
        bookmarks.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([]));
        var outline = Substitute.For<IPdfOutlineReader>();

        CreateVm(bookmarks, outlineReader: null, filePath: "/tmp/x.pdf").CanImportPdfOutline.Should().BeFalse();
        CreateVm(bookmarks, outline, filePath: "/tmp/x.djvu").CanImportPdfOutline.Should().BeFalse();
        CreateVm(bookmarks, outline, filePath: "/tmp/x.pdf").CanImportPdfOutline.Should().BeTrue();
    }
}
