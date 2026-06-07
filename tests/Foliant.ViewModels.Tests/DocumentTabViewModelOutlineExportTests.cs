using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

/// <summary>«Export Bookmarks to PDF» (W7): sidecar-закладки → PDF /Outlines через
/// <see cref="IPdfOutlineWriter"/>. Проверяем конвертацию (PageIndex/Title/Depth), gate
/// (PDF + writer + непустой список) и no-op/swallow поведение — как у соседних PDF-mutate команд.</summary>
public sealed class DocumentTabViewModelOutlineExportTests
{
    private static async Task<DocumentTabViewModel> CreateVm(
        string filePath = "/tmp/x.pdf",
        IPdfOutlineWriter? outlineWriter = null,
        IReadOnlyList<Bookmark>? bookmarks = null)
    {
        var document = Substitute.For<IDocument>();
        document.PageCount.Returns(10);
        document.Metadata.Returns(new DocumentMetadata(null, null, null, null, null, new Dictionary<string, string>()));

        var search = Substitute.For<ISearchService>();
        search.SearchInDocumentAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<IReadOnlyList<SearchHit>>([]));
        var ann = Substitute.For<IAnnotationService>();
        ann.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Annotation>>([]));
        var bm = Substitute.For<IBookmarkService>();
        bm.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(Task.FromResult<IReadOnlyList<Bookmark>>(bookmarks ?? []));

        var vm = new DocumentTabViewModel(
            document, filePath, search, ann, bm,
            NullLogger<DocumentTabViewModel>.Instance,
            outlineWriter: outlineWriter);

        // Seed Bookmarks через тот же путь, что прод-код (LoadBookmarksAsync → IBookmarkService.ListAsync),
        // чтобы CanExecute/CollectionChanged-нотификации сработали так же, как в реальном сценарии.
        if (bookmarks is { Count: > 0 })
        {
            await vm.LoadBookmarksAsync(CancellationToken.None);
        }

        return vm;
    }

    private static IReadOnlyList<Bookmark> SampleBookmarks() =>
    [
        new(Guid.NewGuid(), PageIndex: 0, Label: "Cover", CreatedAt: DateTimeOffset.UnixEpoch, Depth: 0),
        new(Guid.NewGuid(), PageIndex: 3, Label: "Chapter 1", CreatedAt: DateTimeOffset.UnixEpoch, Depth: 0),
        new(Guid.NewGuid(), PageIndex: 4, Label: "Section 1.1", CreatedAt: DateTimeOffset.UnixEpoch, Depth: 1),
    ];

    // ───── CanExportBookmarksToPdf gate ─────

    [Fact]
    public async Task CanExportBookmarksToPdf_WriterNull_False()
    {
        var vm = await CreateVm(outlineWriter: null, bookmarks: SampleBookmarks());
        vm.CanExportBookmarksToPdf.Should().BeFalse();
    }

    [Fact]
    public async Task CanExportBookmarksToPdf_NonPdfSource_False()
    {
        var vm = await CreateVm(filePath: "/tmp/foo.epub",
            outlineWriter: Substitute.For<IPdfOutlineWriter>(), bookmarks: SampleBookmarks());
        vm.CanExportBookmarksToPdf.Should().BeFalse();
    }

    [Fact]
    public async Task CanExportBookmarksToPdf_NoBookmarks_False()
    {
        var vm = await CreateVm(filePath: "/tmp/foo.pdf",
            outlineWriter: Substitute.For<IPdfOutlineWriter>(), bookmarks: []);
        vm.CanExportBookmarksToPdf.Should().BeFalse();
    }

    [Fact]
    public async Task CanExportBookmarksToPdf_PdfWriterAndBookmarks_True()
    {
        var vm = await CreateVm(filePath: "/tmp/foo.PDF",
            outlineWriter: Substitute.For<IPdfOutlineWriter>(), bookmarks: SampleBookmarks());
        vm.CanExportBookmarksToPdf.Should().BeTrue();
    }

    // ───── ExportBookmarksToPdfCommand ─────

    [Fact]
    public async Task ExportBookmarksToPdfCommand_ForwardsConvertedEntries_ToWriter()
    {
        var writer = Substitute.For<IPdfOutlineWriter>();
        var marks = SampleBookmarks();
        var vm = await CreateVm(filePath: "/tmp/in.pdf", outlineWriter: writer, bookmarks: marks);

        await vm.ExportBookmarksToPdfCommand.ExecuteAsync(new ExportOutlineRequest("/tmp/out.pdf"));

        await writer.Received(1).WriteOutlineAsync(
            "/tmp/in.pdf",
            "/tmp/out.pdf",
            Arg.Is<IReadOnlyList<DocumentOutlineEntry>>(entries =>
                entries.Count == 3
                && entries[0].PageIndex == 0 && entries[0].Title == "Cover" && entries[0].Depth == 0
                && entries[1].PageIndex == 3 && entries[1].Title == "Chapter 1" && entries[1].Depth == 0
                && entries[2].PageIndex == 4 && entries[2].Title == "Section 1.1" && entries[2].Depth == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExportBookmarksToPdfCommand_PreservesDepth()
    {
        var writer = Substitute.For<IPdfOutlineWriter>();
        IReadOnlyList<Bookmark> nested =
        [
            new(Guid.NewGuid(), PageIndex: 1, Label: "Nested", CreatedAt: DateTimeOffset.UnixEpoch, Depth: 1),
        ];
        var vm = await CreateVm(filePath: "/tmp/in.pdf", outlineWriter: writer, bookmarks: nested);

        await vm.ExportBookmarksToPdfCommand.ExecuteAsync(new ExportOutlineRequest("/tmp/out.pdf"));

        await writer.Received(1).WriteOutlineAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Is<IReadOnlyList<DocumentOutlineEntry>>(entries =>
                entries.Count == 1 && entries[0].Depth == 1 && entries[0].PageIndex == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExportBookmarksToPdfCommand_OnNonPdf_DoesNotExecute()
    {
        var writer = Substitute.For<IPdfOutlineWriter>();
        var vm = await CreateVm(filePath: "/tmp/in.epub", outlineWriter: writer, bookmarks: SampleBookmarks());

        vm.ExportBookmarksToPdfCommand.CanExecute(new ExportOutlineRequest("/tmp/out.pdf")).Should().BeFalse();
    }

    [Fact]
    public async Task ExportBookmarksToPdfCommand_WriterNull_DoesNotExecute()
    {
        var vm = await CreateVm(filePath: "/tmp/in.pdf", outlineWriter: null, bookmarks: SampleBookmarks());

        vm.CanExportBookmarksToPdf.Should().BeFalse();
        vm.ExportBookmarksToPdfCommand.CanExecute(new ExportOutlineRequest("/tmp/out.pdf")).Should().BeFalse();
    }

    [Fact]
    public async Task ExportBookmarksToPdfCommand_NoBookmarks_DoesNotExecute()
    {
        var writer = Substitute.For<IPdfOutlineWriter>();
        var vm = await CreateVm(filePath: "/tmp/in.pdf", outlineWriter: writer, bookmarks: []);

        vm.ExportBookmarksToPdfCommand.CanExecute(new ExportOutlineRequest("/tmp/out.pdf")).Should().BeFalse();
    }

    [Fact]
    public async Task ExportBookmarksToPdfCommand_BlankTarget_NoOp()
    {
        var writer = Substitute.For<IPdfOutlineWriter>();
        var vm = await CreateVm(filePath: "/tmp/in.pdf", outlineWriter: writer, bookmarks: SampleBookmarks());

        await vm.ExportBookmarksToPdfCommand.ExecuteAsync(new ExportOutlineRequest("   "));

        await writer.DidNotReceiveWithAnyArgs().WriteOutlineAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task ExportBookmarksToPdfCommand_WriterThrows_DoesNotPropagate()
    {
        var writer = Substitute.For<IPdfOutlineWriter>();
        writer.WriteOutlineAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<DocumentOutlineEntry>>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromException(new InvalidOperationException("boom")));
        var vm = await CreateVm(filePath: "/tmp/in.pdf", outlineWriter: writer, bookmarks: SampleBookmarks());

        Func<Task> act = async () => await vm.ExportBookmarksToPdfCommand.ExecuteAsync(new ExportOutlineRequest("/tmp/out.pdf"));

        await act.Should().NotThrowAsync();
    }

    // ───── richness options ─────

    [Fact]
    public async Task ExportBookmarksToPdfCommand_AppliesDestinationMode_ToAllEntries()
    {
        var writer = Substitute.For<IPdfOutlineWriter>();
        var vm = await CreateVm(filePath: "/tmp/in.pdf", outlineWriter: writer, bookmarks: SampleBookmarks());

        await vm.ExportBookmarksToPdfCommand.ExecuteAsync(
            new ExportOutlineRequest("/tmp/out.pdf", OutlineDestinationMode.FitWidth));

        await writer.Received(1).WriteOutlineAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<IReadOnlyList<DocumentOutlineEntry>>(e =>
                e.All(x => x.Destination == OutlineDestinationMode.FitWidth)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExportBookmarksToPdfCommand_CollapseNested_SetsIsOpenFalse()
    {
        var writer = Substitute.For<IPdfOutlineWriter>();
        var vm = await CreateVm(filePath: "/tmp/in.pdf", outlineWriter: writer, bookmarks: SampleBookmarks());

        await vm.ExportBookmarksToPdfCommand.ExecuteAsync(
            new ExportOutlineRequest("/tmp/out.pdf", CollapseNested: true));

        await writer.Received(1).WriteOutlineAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<IReadOnlyList<DocumentOutlineEntry>>(e => e.All(x => !x.IsOpen)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExportBookmarksToPdfCommand_BoldTopLevel_OnlyDepthZeroBold()
    {
        var writer = Substitute.For<IPdfOutlineWriter>();
        var vm = await CreateVm(filePath: "/tmp/in.pdf", outlineWriter: writer, bookmarks: SampleBookmarks());

        await vm.ExportBookmarksToPdfCommand.ExecuteAsync(
            new ExportOutlineRequest("/tmp/out.pdf", BoldTopLevel: true));

        await writer.Received(1).WriteOutlineAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<IReadOnlyList<DocumentOutlineEntry>>(e =>
                e.Where(x => x.Depth == 0).All(x => x.IsBold)
                && e.Where(x => x.Depth > 0).All(x => !x.IsBold)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExportBookmarksToPdfCommand_DefaultRequest_FitPageExpandedPlain()
    {
        var writer = Substitute.For<IPdfOutlineWriter>();
        var vm = await CreateVm(filePath: "/tmp/in.pdf", outlineWriter: writer, bookmarks: SampleBookmarks());

        await vm.ExportBookmarksToPdfCommand.ExecuteAsync(new ExportOutlineRequest("/tmp/out.pdf"));

        await writer.Received(1).WriteOutlineAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<IReadOnlyList<DocumentOutlineEntry>>(e =>
                e.All(x => x.Destination == OutlineDestinationMode.FitPage && x.IsOpen && !x.IsBold && !x.IsItalic)),
            Arg.Any<CancellationToken>());
    }
}
