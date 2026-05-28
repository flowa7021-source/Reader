using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelPageImageExportTests
{
    private static DocumentTabViewModel CreateVm(IPageImageExporter? exporter, int pageCount = 5, string filePath = "/tmp/doc.pdf")
    {
        var doc = Substitute.For<IDocument>();
        doc.PageCount.Returns(pageCount);
        doc.Metadata.Returns(new DocumentMetadata(null, null, null, null, null, new Dictionary<string, string>()));

        var bookmarks = Substitute.For<IBookmarkService>();
        bookmarks.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([]));

        return new DocumentTabViewModel(
            doc,
            filePath,
            Substitute.For<ISearchService>(),
            Substitute.For<IAnnotationService>(),
            bookmarks,
            NullLogger<DocumentTabViewModel>.Instance,
            pageImageExporter: exporter);
    }

    [Fact]
    public void CanExportCurrentPageAsImage_FalseWithoutExporter_TrueWithExporterAndPages()
    {
        var exporter = Substitute.For<IPageImageExporter>();

        CreateVm(exporter: null).CanExportCurrentPageAsImage.Should().BeFalse();
        CreateVm(exporter, pageCount: 0).CanExportCurrentPageAsImage.Should().BeFalse();
        CreateVm(exporter, pageCount: 3).CanExportCurrentPageAsImage.Should().BeTrue();
    }

    [Fact]
    public async Task ExportCurrentPageAsImage_PassesCurrentPageAndZoomThrough()
    {
        var exporter = Substitute.For<IPageImageExporter>();
        var vm = CreateVm(exporter, pageCount: 10);
        vm.CurrentPageIndex = 4;
        vm.Zoom = 1.5;

        await vm.ExportCurrentPageAsImageCommand.ExecuteAsync("/tmp/out.png");

        await exporter.Received().ExportAsync(
            Arg.Any<IDocument>(), 4, 1.5, "/tmp/out.png", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExportCurrentPageAsImage_EmptyPath_IsNoOp()
    {
        var exporter = Substitute.For<IPageImageExporter>();
        var vm = CreateVm(exporter);

        await vm.ExportCurrentPageAsImageCommand.ExecuteAsync("   ");

        await exporter.DidNotReceive().ExportAsync(
            Arg.Any<IDocument>(), Arg.Any<int>(), Arg.Any<double>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExportCurrentPageAsImage_ExporterThrows_TabSurvives_LogsAndContinues()
    {
        var exporter = Substitute.For<IPageImageExporter>();
        exporter.ExportAsync(
                Arg.Any<IDocument>(), Arg.Any<int>(), Arg.Any<double>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new IOException("disk full"));
        var vm = CreateVm(exporter);

        // Команда не должна пробрасывать исключение наверх — иначе UI обработчик пробьётся.
        await vm.ExportCurrentPageAsImageCommand.ExecuteAsync("/tmp/out.png");

        // Tab остался жив; следующий вызов должен опять попытаться.
        await exporter.Received(1).ExportAsync(
            Arg.Any<IDocument>(), Arg.Any<int>(), Arg.Any<double>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
