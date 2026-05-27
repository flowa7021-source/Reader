using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelExportAnnotatedPdfTests
{
    private static DocumentTabViewModel CreateVm(
        IAnnotationService annotations,
        IAnnotatedPdfExportService exporter,
        string filePath = "/tmp/doc.pdf")
    {
        var doc = Substitute.For<IDocument>();
        doc.PageCount.Returns(3);

        return new DocumentTabViewModel(
            doc,
            filePath,
            Substitute.For<ISearchService>(),
            annotations,
            Substitute.For<IBookmarkService>(),
            NullLogger<DocumentTabViewModel>.Instance,
            annotatedPdfExporter: exporter);
    }

    [Fact]
    public async Task Export_WithAnnotations_CallsServiceWithSourceAnnotationsAndTarget()
    {
        var exporter = Substitute.For<IAnnotatedPdfExportService>();
        var vm = CreateVm(Substitute.For<IAnnotationService>(), exporter);
        await vm.AddHighlightAsync(0, new AnnotationRect(1, 2, 3, 4), "#FF0000", CancellationToken.None);

        vm.CanExportAnnotatedPdf.Should().BeTrue();
        await vm.ExportAnnotatedPdfCommand.ExecuteAsync("/tmp/out.pdf");

        await exporter.Received(1).ExportAsync(
            "/tmp/doc.pdf",
            Arg.Is<IReadOnlyList<Annotation>>(a => a.Count == 1 && a[0].Kind == AnnotationKind.Highlight),
            "/tmp/out.pdf",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Export_NoAnnotations_CannotExport_AndIsNoOp()
    {
        var exporter = Substitute.For<IAnnotatedPdfExportService>();
        var vm = CreateVm(Substitute.For<IAnnotationService>(), exporter);

        vm.CanExportAnnotatedPdf.Should().BeFalse();
        await vm.ExportAnnotatedPdfCommand.ExecuteAsync("/tmp/out.pdf");

        await exporter.DidNotReceive().ExportAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<Annotation>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Export_NonPdfSource_CannotExport_AndIsNoOp()
    {
        var exporter = Substitute.For<IAnnotatedPdfExportService>();
        var vm = CreateVm(Substitute.For<IAnnotationService>(), exporter, "/tmp/doc.djvu");
        await vm.AddHighlightAsync(0, new AnnotationRect(1, 2, 3, 4), "#FF0000", CancellationToken.None);

        vm.CanExportAnnotatedPdf.Should().BeFalse();
        await vm.ExportAnnotatedPdfCommand.ExecuteAsync("/tmp/out.pdf");

        await exporter.DidNotReceive().ExportAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<Annotation>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Export_EmptyTargetPath_IsNoOp()
    {
        var exporter = Substitute.For<IAnnotatedPdfExportService>();
        var vm = CreateVm(Substitute.For<IAnnotationService>(), exporter);
        await vm.AddHighlightAsync(0, new AnnotationRect(1, 2, 3, 4), "#FF0000", CancellationToken.None);

        await vm.ExportAnnotatedPdfCommand.ExecuteAsync("   ");

        await exporter.DidNotReceive().ExportAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<Annotation>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CanExportAnnotatedPdf_False_WhenNoExporterService()
    {
        var doc = Substitute.For<IDocument>();
        doc.PageCount.Returns(1);
        var vm = new DocumentTabViewModel(
            doc,
            "/tmp/doc.pdf",
            Substitute.For<ISearchService>(),
            Substitute.For<IAnnotationService>(),
            Substitute.For<IBookmarkService>(),
            NullLogger<DocumentTabViewModel>.Instance);
        await vm.AddHighlightAsync(0, new AnnotationRect(1, 2, 3, 4), "#FF0000", CancellationToken.None);

        vm.CanExportAnnotatedPdf.Should().BeFalse();
    }
}
