using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelFontsTests
{
    private static readonly DocumentMetadata SampleMetadata = new(
        Title: "t", Author: "a", Subject: "s",
        Created: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Modified: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Custom: new Dictionary<string, string>());

    private static DocumentTabViewModel CreateVm(string filePath = "/tmp/x.pdf", IPdfFontService? svc = null)
    {
        var document = Substitute.For<IDocument>();
        document.PageCount.Returns(3);
        document.Metadata.Returns(SampleMetadata);
        var search = Substitute.For<ISearchService>();
        search.SearchInDocumentAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<IReadOnlyList<SearchHit>>([]));
        var ann = Substitute.For<IAnnotationService>();
        ann.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Annotation>>([]));
        var bm = Substitute.For<IBookmarkService>();
        bm.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Bookmark>>([]));
        return new DocumentTabViewModel(document, filePath, search, ann, bm,
            NullLogger<DocumentTabViewModel>.Instance, fontService: svc);
    }

    private static IReadOnlyList<PdfFontInfo> Sample() =>
        [new PdfFontInfo("ABCDEF+Arial", "TrueType", true), new PdfFontInfo("Helvetica", "Type1", false)];

    [Fact]
    public void CanListFonts_NoService_False() => CreateVm(svc: null).CanListFonts.Should().BeFalse();

    [Fact]
    public void CanListFonts_NonPdf_False() =>
        CreateVm(filePath: "/tmp/f.djvu", svc: Substitute.For<IPdfFontService>()).CanListFonts.Should().BeFalse();

    [Fact]
    public void CanListFonts_PdfAndService_True() =>
        CreateVm(filePath: "/tmp/f.PDF", svc: Substitute.For<IPdfFontService>()).CanListFonts.Should().BeTrue();

    [Fact]
    public async Task LoadFontsCommand_PopulatesSnapshot()
    {
        var svc = Substitute.For<IPdfFontService>();
        svc.ListFontsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(Sample()));
        var vm = CreateVm(filePath: "/tmp/in.pdf", svc: svc);

        await vm.LoadFontsCommand.ExecuteAsync(null);

        vm.CurrentFonts.Should().Equal(Sample());
    }

    [Fact]
    public async Task LoadFontsCommand_ServiceThrows_LeavesEmpty()
    {
        var svc = Substitute.For<IPdfFontService>();
        svc.ListFontsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromException<IReadOnlyList<PdfFontInfo>>(new InvalidOperationException("boom")));
        var vm = CreateVm(filePath: "/tmp/in.pdf", svc: svc);

        Func<Task> act = async () => await vm.LoadFontsCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync();
        vm.CurrentFonts.Should().BeEmpty();
    }
}
