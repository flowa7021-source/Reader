using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelBookmarkExportTests : IDisposable
{
    private readonly string _tmpDir;

    public DocumentTabViewModelBookmarkExportTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "foliant-bm-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tmpDir, recursive: true);
        }
        catch
        {
            /* best-effort */
        }
    }

    private static DocumentTabViewModel CreateVm(
        IBookmarkService bookmarks,
        IBookmarkFormatCatalog? catalog)
    {
        var doc = Substitute.For<IDocument>();
        doc.PageCount.Returns(10);
        doc.Metadata.Returns(new DocumentMetadata(null, null, null, null, null, new Dictionary<string, string>()));

        return new DocumentTabViewModel(
            doc,
            "/tmp/doc.pdf",
            Substitute.For<ISearchService>(),
            Substitute.For<IAnnotationService>(),
            bookmarks,
            NullLogger<DocumentTabViewModel>.Instance,
            bookmarkFormats: catalog);
    }

    private static IBookmarkFormatCatalog RealCatalog() =>
        new BookmarkFormatCatalog(
            [new JsonBookmarkExporter(), new MarkdownBookmarkExporter()],
            [new JsonBookmarkImporter()]);

    [Fact]
    public void CanExportBookmarks_FalseWithoutCatalog_FalseWhenEmpty_TrueOtherwise()
    {
        var bookmarks = Substitute.For<IBookmarkService>();
        bookmarks.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([]));

        var noCatalog = CreateVm(bookmarks, catalog: null);
        noCatalog.CanExportBookmarks.Should().BeFalse();

        var withCatalog = CreateVm(bookmarks, RealCatalog());
        withCatalog.CanExportBookmarks.Should().BeFalse(); // empty bookmarks

        withCatalog.Bookmarks.Add(new Bookmark(Guid.NewGuid(), 0, "p1", DateTimeOffset.UnixEpoch));
        withCatalog.CanExportBookmarks.Should().BeTrue();
    }

    [Fact]
    public async Task ExportBookmarks_ResolvesFormatByExtension_WritesJsonToDisk()
    {
        var bookmarks = Substitute.For<IBookmarkService>();
        bookmarks.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([]));
        var vm = CreateVm(bookmarks, RealCatalog());
        vm.Bookmarks.Add(new Bookmark(Guid.NewGuid(), 2, "Chapter 1", DateTimeOffset.UnixEpoch));
        vm.Bookmarks.Add(new Bookmark(Guid.NewGuid(), 5, "Chapter 2", DateTimeOffset.UnixEpoch));

        string target = Path.Combine(_tmpDir, "out.json");
        await vm.ExportBookmarksCommand.ExecuteAsync(target);

        File.Exists(target).Should().BeTrue();
        string text = await File.ReadAllTextAsync(target);
        text.Should().Contain("Chapter 1").And.Contain("Chapter 2");
    }

    [Fact]
    public async Task ExportBookmarks_MdExtension_ProducesMarkdown()
    {
        var bookmarks = Substitute.For<IBookmarkService>();
        bookmarks.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([]));
        var vm = CreateVm(bookmarks, RealCatalog());
        vm.Bookmarks.Add(new Bookmark(Guid.NewGuid(), 0, "First", DateTimeOffset.UnixEpoch));

        string target = Path.Combine(_tmpDir, "out.md");
        await vm.ExportBookmarksCommand.ExecuteAsync(target);

        string text = await File.ReadAllTextAsync(target);
        text.Should().Contain("First");
        text.Should().Contain("#"); // markdown heading marker
        text.Should().NotContain("\"id\""); // not JSON
    }

    [Fact]
    public async Task ExportBookmarks_UnknownExtension_LogsAndNoOps()
    {
        var bookmarks = Substitute.For<IBookmarkService>();
        bookmarks.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([]));
        var vm = CreateVm(bookmarks, RealCatalog());
        vm.Bookmarks.Add(new Bookmark(Guid.NewGuid(), 0, "First", DateTimeOffset.UnixEpoch));

        string target = Path.Combine(_tmpDir, "out.unknownext");
        await vm.ExportBookmarksCommand.ExecuteAsync(target);

        File.Exists(target).Should().BeFalse(); // no exporter → no write
    }

    [Fact]
    public async Task ImportBookmarks_FromJson_AddsEachViaService()
    {
        var bookmarks = Substitute.For<IBookmarkService>();
        bookmarks.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([]));
        bookmarks.AddAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(callInfo => Task.FromResult(new Bookmark(Guid.NewGuid(), callInfo.ArgAt<int>(1), callInfo.ArgAt<string>(2), DateTimeOffset.UnixEpoch)));
        var vm = CreateVm(bookmarks, RealCatalog());

        // Build payload via the exporter — keeps test wire-compatible with the real format.
        string payload = new JsonBookmarkExporter().Export(
        [
            new Bookmark(Guid.NewGuid(), 1, "Intro", DateTimeOffset.UnixEpoch),
            new Bookmark(Guid.NewGuid(), 7, "Outro", DateTimeOffset.UnixEpoch),
        ]);
        string source = Path.Combine(_tmpDir, "in.json");
        await File.WriteAllTextAsync(source, payload);

        await vm.ImportBookmarksCommand.ExecuteAsync(source);

        await bookmarks.Received().AddAsync(Arg.Any<string>(), 1, "Intro", Arg.Any<CancellationToken>());
        await bookmarks.Received().AddAsync(Arg.Any<string>(), 7, "Outro", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportBookmarks_UnknownExtension_NoOps()
    {
        var bookmarks = Substitute.For<IBookmarkService>();
        bookmarks.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([]));
        var vm = CreateVm(bookmarks, RealCatalog());

        string source = Path.Combine(_tmpDir, "bogus.xyz");
        await File.WriteAllTextAsync(source, "garbage");

        await vm.ImportBookmarksCommand.ExecuteAsync(source);

        await bookmarks.DidNotReceive().AddAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
