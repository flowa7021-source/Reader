using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelFormFillTests
{
    private static DocumentTabViewModel CreateVm(
        string filePath = "/tmp/x.pdf",
        IPdfFormReader? reader = null,
        IPdfFormFillService? fill = null)
    {
        var document = Substitute.For<IDocument>();
        document.PageCount.Returns(1);
        document.Metadata.Returns(new DocumentMetadata(null, null, null, null, null, new Dictionary<string, string>()));

        var search = Substitute.For<ISearchService>();
        search.SearchInDocumentAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<IReadOnlyList<SearchHit>>([]));
        var ann = Substitute.For<IAnnotationService>();
        ann.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Annotation>>([]));
        var bm = Substitute.For<IBookmarkService>();
        bm.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([]));

        return new DocumentTabViewModel(
            document, filePath, search, ann, bm,
            NullLogger<DocumentTabViewModel>.Instance,
            pdfFormReader: reader,
            pdfFormFillService: fill);
    }

    [Fact]
    public void CanFillForm_TrueWhenReaderFillAndPdf()
    {
        var vm = CreateVm(
            reader: Substitute.For<IPdfFormReader>(),
            fill: Substitute.For<IPdfFormFillService>());

        vm.CanFillForm.Should().BeTrue();
    }

    [Fact]
    public void CanFillForm_FalseWithoutFillService()
    {
        var vm = CreateVm(reader: Substitute.For<IPdfFormReader>(), fill: null);

        vm.CanFillForm.Should().BeFalse();
    }

    [Fact]
    public void CanFillForm_FalseForNonPdf()
    {
        var vm = CreateVm(
            filePath: "/tmp/x.epub",
            reader: Substitute.For<IPdfFormReader>(),
            fill: Substitute.For<IPdfFormFillService>());

        vm.CanFillForm.Should().BeFalse();
    }

    [Fact]
    public async Task ReadFormFieldsAsync_ReturnsSortedEntries()
    {
        var reader = Substitute.For<IPdfFormReader>();
        reader.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<IReadOnlyDictionary<string, string>>(
                  new Dictionary<string, string> { ["zname"] = "Z", ["aname"] = "A" }));
        var vm = CreateVm(reader: reader, fill: Substitute.For<IPdfFormFillService>());

        var fields = await vm.ReadFormFieldsAsync(CancellationToken.None);

        fields.Should().HaveCount(2);
        fields[0].Name.Should().Be("aname");
        fields[0].Value.Should().Be("A");
        fields[1].Name.Should().Be("zname");
    }

    [Fact]
    public async Task ReadFormFieldsAsync_NoReader_ReturnsEmpty()
    {
        var vm = CreateVm(reader: null, fill: Substitute.For<IPdfFormFillService>());

        var fields = await vm.ReadFormFieldsAsync(CancellationToken.None);

        fields.Should().BeEmpty();
    }

    [Fact]
    public async Task FillForm_AppliesEditedValuesToTargetPdf()
    {
        var reader = Substitute.For<IPdfFormReader>();
        var fill = Substitute.For<IPdfFormFillService>();
        var vm = CreateVm(reader: reader, fill: fill);

        var request = new FillFormRequest(
            [new FormFieldEntry("name", "Alice"), new FormFieldEntry("city", "Moscow")],
            "/tmp/out.pdf");
        await vm.FillFormCommand.ExecuteAsync(request);

        await fill.Received(1).ApplyAsync(
            "/tmp/x.pdf",
            Arg.Is<IReadOnlyDictionary<string, string>>(d =>
                d["name"] == "Alice" && d["city"] == "Moscow"),
            "/tmp/out.pdf",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FillForm_BlankTarget_NoOp()
    {
        var fill = Substitute.For<IPdfFormFillService>();
        var vm = CreateVm(reader: Substitute.For<IPdfFormReader>(), fill: fill);

        await vm.FillFormCommand.ExecuteAsync(new FillFormRequest([new FormFieldEntry("a", "1")], "   "));

        await fill.DidNotReceive().ApplyAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void FormFieldEntry_ValueIsMutable()
    {
        var entry = new FormFieldEntry("field", "old");
        entry.Value = "new";

        entry.Name.Should().Be("field");
        entry.Value.Should().Be("new");
    }
}
