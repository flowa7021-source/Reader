using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Application.Settings;
using Foliant.Application.UseCases;
using Foliant.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class MainViewModelTests
{
    private static MainViewModel CreateVm(
        IRecentsService? recents = null,
        ISettingsService? settings = null,
        ILocalizationService? localization = null,
        IDocumentIndexer? indexer = null)
    {
        var useCase = new OpenDocumentUseCase([], NullLogger<OpenDocumentUseCase>.Instance);
        Func<IDocument, string, DocumentTabViewModel> factory = (_, _) => throw new NotSupportedException();

        // Дефолты применяются ТОЛЬКО если caller не передал свой настроенный mock —
        // иначе кастомные .Returns(...) будут переписаны хелпером.
        if (recents is null)
        {
            recents = Substitute.For<IRecentsService>();
            recents.GetAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());
        }

        if (settings is null)
        {
            settings = Substitute.For<ISettingsService>();
            settings.Current.Returns(AppSettings.Default);
        }

        localization ??= Substitute.For<ILocalizationService>();
        indexer ??= Substitute.For<IDocumentIndexer>();

        return new MainViewModel(useCase, factory, recents, settings, localization, indexer, NullLogger<MainViewModel>.Instance);
    }

    [Fact]
    public void Title_DefaultsToFoliant()
    {
        var vm = CreateVm();

        vm.Title.Should().Be("Foliant");
    }

    [Fact]
    public void StatusMessage_RaisesPropertyChanged_WhenSet()
    {
        var vm = CreateVm();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.StatusMessage))
            {
                raised = true;
            }
        };

        vm.StatusMessage = "Loading...";

        raised.Should().BeTrue();
        vm.StatusMessage.Should().Be("Loading...");
    }

    [Fact]
    public async Task InitializeAsync_PopulatesRecentFilesFromService()
    {
        var recents = Substitute.For<IRecentsService>();
        recents.GetAsync(Arg.Any<CancellationToken>()).Returns(new[] { "a.pdf", "b.pdf" });
        var vm = CreateVm(recents);

        await vm.InitializeAsync(default);

        vm.RecentFiles.Should().BeEquivalentTo(["a.pdf", "b.pdf"]);
    }

    [Fact]
    public async Task InitializeAsync_AppliesThemeFromSettings()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(AppSettings.Default with { Theme = "Dark" });
        var vm = CreateVm(settings: settings);

        await vm.InitializeAsync(default);

        vm.CurrentTheme.Should().Be("Dark");
    }

    [Fact]
    public async Task InitializeAsync_AppliesCultureFromSettings()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(AppSettings.Default with { Language = "en" });
        var localization = Substitute.For<ILocalizationService>();
        var vm = CreateVm(settings: settings, localization: localization);

        await vm.InitializeAsync(default);

        localization.Received(1).SetCulture("en");
    }

    [Fact]
    public async Task ClearRecentsCommand_CallsServiceAndEmptiesCollection()
    {
        var recents = Substitute.For<IRecentsService>();
        var first = true;
        recents.GetAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (first)
            {
                first = false;
                return new[] { "a.pdf" };
            }
            return Array.Empty<string>();
        });
        var vm = CreateVm(recents);
        await vm.InitializeAsync(default);

        await vm.ClearRecentsCommand.ExecuteAsync(null);

        await recents.Received(1).ClearAsync(Arg.Any<CancellationToken>());
        vm.RecentFiles.Should().BeEmpty();
    }

    // ───── Multi-tab navigation (S11/E) ─────

    [Fact]
    public void NextTabCommand_CyclesForward()
    {
        var vm = CreateVm();
        var t0 = MakeTabStub();
        var t1 = MakeTabStub();
        var t2 = MakeTabStub();
        vm.Tabs.Add(t0);
        vm.Tabs.Add(t1);
        vm.Tabs.Add(t2);
        vm.SelectedTab = t0;

        vm.NextTabCommand.Execute(null);
        vm.SelectedTab.Should().BeSameAs(t1);

        vm.NextTabCommand.Execute(null);
        vm.SelectedTab.Should().BeSameAs(t2);

        vm.NextTabCommand.Execute(null);
        vm.SelectedTab.Should().BeSameAs(t0);   // wrap
    }

    [Fact]
    public void PreviousTabCommand_CyclesBackward()
    {
        var vm = CreateVm();
        var t0 = MakeTabStub();
        var t1 = MakeTabStub();
        var t2 = MakeTabStub();
        vm.Tabs.Add(t0);
        vm.Tabs.Add(t1);
        vm.Tabs.Add(t2);
        vm.SelectedTab = t0;

        vm.PreviousTabCommand.Execute(null);
        vm.SelectedTab.Should().BeSameAs(t2);   // wrap

        vm.PreviousTabCommand.Execute(null);
        vm.SelectedTab.Should().BeSameAs(t1);
    }

    [Fact]
    public void NextTabCommand_SingleTab_NoOp()
    {
        var vm = CreateVm();
        var t = MakeTabStub();
        vm.Tabs.Add(t);
        vm.SelectedTab = t;

        vm.NextTabCommand.Execute(null);

        vm.SelectedTab.Should().BeSameAs(t);
    }

    [Fact]
    public void NextTabCommand_NoTabs_NoOp()
    {
        var vm = CreateVm();

        var act = () => vm.NextTabCommand.Execute(null);
        act.Should().NotThrow();
        vm.SelectedTab.Should().BeNull();
    }

    [Fact]
    public async Task CloseCurrentTabCommand_RemovesActive_SelectsNeighbor()
    {
        var vm = CreateVm();
        var t0 = MakeTabStub();
        var t1 = MakeTabStub();
        var t2 = MakeTabStub();
        vm.Tabs.Add(t0);
        vm.Tabs.Add(t1);
        vm.Tabs.Add(t2);
        vm.SelectedTab = t1;

        await vm.CloseCurrentTabCommand.ExecuteAsync(null);

        vm.Tabs.Should().HaveCount(2);
        vm.Tabs.Should().NotContain(t1);
        // Closing index 1 of [t0,t1,t2] -> remaining [t0,t2], next = min(1, 1) = 1 -> t2.
        vm.SelectedTab.Should().BeSameAs(t2);
    }

    [Fact]
    public async Task CloseCurrentTabCommand_LastTab_LeavesNullSelected()
    {
        var vm = CreateVm();
        var t = MakeTabStub();
        vm.Tabs.Add(t);
        vm.SelectedTab = t;

        await vm.CloseCurrentTabCommand.ExecuteAsync(null);

        vm.Tabs.Should().BeEmpty();
        vm.SelectedTab.Should().BeNull();
    }

    [Fact]
    public void SelectTabByNumberCommand_OneBased_SelectsCorrectTab()
    {
        var vm = CreateVm();
        var t0 = MakeTabStub();
        var t1 = MakeTabStub();
        var t2 = MakeTabStub();
        vm.Tabs.Add(t0);
        vm.Tabs.Add(t1);
        vm.Tabs.Add(t2);
        vm.SelectedTab = t0;

        vm.SelectTabByNumberCommand.Execute(2);

        vm.SelectedTab.Should().BeSameAs(t1);
    }

    [Fact]
    public void SelectTabByNumberCommand_OutOfRange_NoOp()
    {
        var vm = CreateVm();
        var t = MakeTabStub();
        vm.Tabs.Add(t);
        vm.SelectedTab = t;

        vm.SelectTabByNumberCommand.Execute(5);   // вкладки 5 нет

        vm.SelectedTab.Should().BeSameAs(t);
    }

    [Fact]
    public void SelectTabByNumberCommand_ZeroOrNegative_NoOp()
    {
        var vm = CreateVm();
        var t = MakeTabStub();
        vm.Tabs.Add(t);
        vm.SelectedTab = t;

        vm.SelectTabByNumberCommand.Execute(0);
        vm.SelectedTab.Should().BeSameAs(t);

        vm.SelectTabByNumberCommand.Execute(-3);
        vm.SelectedTab.Should().BeSameAs(t);
    }

    private static DocumentTabViewModel MakeTabStub()
    {
        var doc = Substitute.For<IDocument>();
        doc.PageCount.Returns(1);
        var search = Substitute.For<ISearchService>();
        search.SearchInDocumentAsync(Arg.Any<IDocument>(), Arg.Any<string>(), Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<IReadOnlyList<SearchHit>>([]));
        var ann = Substitute.For<IAnnotationService>();
        ann.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IReadOnlyList<Annotation>>([]));
        var bm = Substitute.For<IBookmarkService>();
        bm.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
          .Returns(Task.FromResult<IReadOnlyList<Bookmark>>([]));
        return new DocumentTabViewModel(doc, "/tmp/x.pdf", search, ann, bm, NullLogger<DocumentTabViewModel>.Instance);
    }
}
