using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelSearchHighlightTests
{
    private static DocumentTabViewModel CreateVm(TextLayer pageZeroLayer)
    {
        var document = Substitute.For<IDocument>();
        document.PageCount.Returns(5);
        document.Metadata.Returns(new DocumentMetadata(null, null, null, null, null, new Dictionary<string, string>()));
        document.GetTextLayerAsync(0, Arg.Any<CancellationToken>()).Returns(Task.FromResult<TextLayer?>(pageZeroLayer));
        document.GetTextLayerAsync(Arg.Is<int>(i => i != 0), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<TextLayer?>(null));

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

    private static TextLayer LayerWith(params string[] lines)
    {
        var runs = new List<TextRun>();
        for (int i = 0; i < lines.Length; i++)
        {
            runs.Add(new TextRun(lines[i], X: 10, Y: 100 - (i * 12), W: 200, H: 10));
        }
        return new TextLayer(0, runs);
    }

    [Fact]
    public async Task RunSearch_PopulatesHighlights_ForMatchingLinesOfCurrentPage()
    {
        var vm = CreateVm(LayerWith("the cat sat", "on the mat", "a CAT returns"));
        vm.SearchText = "cat";

        await vm.RunSearchCommand.ExecuteAsync(null);

        vm.CurrentPageSearchHighlights.Should().HaveCount(2); // "the cat sat" + "a CAT returns" (case-insensitive)
        vm.CurrentPageSearchHighlights[0].Should().Be(new AnnotationRect(10, 100, 200, 10));
    }

    [Fact]
    public async Task RunSearch_WithNoMatch_LeavesHighlightsEmpty()
    {
        var vm = CreateVm(LayerWith("alpha", "beta"));
        vm.SearchText = "gamma";

        await vm.RunSearchCommand.ExecuteAsync(null);

        vm.CurrentPageSearchHighlights.Should().BeEmpty();
    }

    [Fact]
    public async Task RunSearch_ThenEmptyQuery_ClearsHighlights()
    {
        var vm = CreateVm(LayerWith("the cat sat"));
        vm.SearchText = "cat";
        await vm.RunSearchCommand.ExecuteAsync(null);
        vm.CurrentPageSearchHighlights.Should().HaveCount(1);

        vm.SearchText = "   ";
        await vm.RunSearchCommand.ExecuteAsync(null);

        vm.CurrentPageSearchHighlights.Should().BeEmpty();
    }

    [Fact]
    public async Task RunSearch_WholeWord_ExcludesSubstringOnlyLines()
    {
        var vm = CreateVm(LayerWith("the cat sat", "category here"));
        vm.SearchMatchWholeWord = true;
        vm.SearchText = "cat";

        await vm.RunSearchCommand.ExecuteAsync(null);

        vm.CurrentPageSearchHighlights.Should().HaveCount(1); // only "the cat sat"
    }
}
