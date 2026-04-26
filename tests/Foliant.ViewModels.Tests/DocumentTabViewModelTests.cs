using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class DocumentTabViewModelTests
{
    private static DocumentTabViewModel CreateVm(
        IDocument? document = null,
        ISearchService? search = null,
        string filePath = "/tmp/x.pdf")
    {
        document ??= Substitute.For<IDocument>();
        document.PageCount.Returns(10);

        search ??= Substitute.For<ISearchService>();
        search
            .SearchInDocumentAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SearchHit>>([]));

        return new DocumentTabViewModel(document, filePath, search, NullLogger<DocumentTabViewModel>.Instance);
    }

    [Fact]
    public void Title_IsFileNameOfPath()
    {
        var vm = CreateVm(filePath: "/tmp/document.pdf");

        vm.Title.Should().Be("document.pdf");
    }

    [Fact]
    public void ToggleSearchCommand_FlipsIsSearchVisible()
    {
        var vm = CreateVm();
        vm.IsSearchVisible.Should().BeFalse();

        vm.ToggleSearchCommand.Execute(null);
        vm.IsSearchVisible.Should().BeTrue();

        vm.ToggleSearchCommand.Execute(null);
        vm.IsSearchVisible.Should().BeFalse();
    }

    [Fact]
    public async Task RunSearchCommand_EmptyText_ClearsResults_DoesNotCallService()
    {
        var search = Substitute.For<ISearchService>();
        var vm = CreateVm(search: search);
        vm.SearchResults.Add(new SearchHit("", "/x.pdf", 0, "stale", 1.0));
        vm.SearchText = "";

        await vm.RunSearchCommand.ExecuteAsync(null);

        vm.SearchResults.Should().BeEmpty();
        await search.DidNotReceive().SearchInDocumentAsync(
            Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunSearchCommand_PopulatesResultsFromService()
    {
        var hits = new SearchHit[]
        {
            new("", "/x.pdf", 1, "first hit", 1.0),
            new("", "/x.pdf", 4, "second hit", 1.0),
        };
        var search = Substitute.For<ISearchService>();
        search
            .SearchInDocumentAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SearchHit>>(hits));
        var vm = CreateVm(search: search);
        vm.SearchText = "hit";

        await vm.RunSearchCommand.ExecuteAsync(null);

        vm.SearchResults.Should().HaveCount(2);
        vm.SearchResults[0].PageIndex.Should().Be(1);
        vm.SearchResults[1].PageIndex.Should().Be(4);
    }

    [Fact]
    public void SelectingSearchHit_UpdatesCurrentPageIndex()
    {
        var vm = CreateVm();
        var hit = new SearchHit("", "/x.pdf", 7, "snippet", 1.0);

        vm.SelectedSearchHit = hit;

        vm.CurrentPageIndex.Should().Be(7);
    }
}
