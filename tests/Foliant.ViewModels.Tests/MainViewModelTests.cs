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
        IDocumentIndexer? indexer = null,
        ILicenseManager? licenseManager = null)
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

        return new MainViewModel(useCase, factory, recents, settings, localization, indexer,
            NullLogger<MainViewModel>.Instance, licenseManager);
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

    // ───── TabsCount + HasOpenTab (S11/O) ─────

    [Fact]
    public void TabsCount_FollowsTabsCollection()
    {
        var vm = CreateVm();
        vm.TabsCount.Should().Be(0);
        vm.HasOpenTab.Should().BeFalse();

        vm.Tabs.Add(MakeTabStub());
        vm.TabsCount.Should().Be(1);
        vm.HasOpenTab.Should().BeTrue();

        vm.Tabs.Add(MakeTabStub());
        vm.Tabs.Add(MakeTabStub());
        vm.TabsCount.Should().Be(3);
    }

    [Fact]
    public async Task TabsCount_FiresPropertyChanged_OnAddRemove()
    {
        var vm = CreateVm();
        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        var t = MakeTabStub();
        vm.Tabs.Add(t);
        fired.Should().Contain(nameof(MainViewModel.TabsCount));
        fired.Should().Contain(nameof(MainViewModel.HasOpenTab));

        fired.Clear();
        vm.SelectedTab = t;
        await vm.CloseCurrentTabCommand.ExecuteAsync(null);

        fired.Should().Contain(nameof(MainViewModel.TabsCount));
        fired.Should().Contain(nameof(MainViewModel.HasOpenTab));
    }

    // ───── RemoveRecent (S11/V) ─────

    [Fact]
    public async Task RemoveRecentCommand_DelegatesToService_AndRefreshes()
    {
        var recents = Substitute.For<IRecentsService>();
        recents.GetAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());
        var vm = CreateVm(recents);

        await vm.RemoveRecentCommand.ExecuteAsync("a.pdf");

        await recents.Received(1).RemoveAsync("a.pdf", Arg.Any<CancellationToken>());
        await recents.Received().GetAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RemoveRecentCommand_NullOrWhitespace_NoOp(string? input)
    {
        var recents = Substitute.For<IRecentsService>();
        recents.GetAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());
        var vm = CreateVm(recents);

        await vm.RemoveRecentCommand.ExecuteAsync(input);

        await recents.DidNotReceive().RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ───── RemoveMissingRecents (S11/Y) ─────

    [Fact]
    public async Task RemoveMissingRecentsCommand_DropsNonexistentPaths_KeepsExisting()
    {
        var existing = Path.Combine(Path.GetTempPath(), $"foliant-rmr-{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(existing, string.Empty);
        try
        {
            var missing = "/definitely/not/here-" + Guid.NewGuid().ToString("N") + ".pdf";
            var recents = Substitute.For<IRecentsService>();
            recents.GetAsync(Arg.Any<CancellationToken>()).Returns(new[] { existing, missing });
            var vm = CreateVm(recents);

            await vm.RemoveMissingRecentsCommand.ExecuteAsync(null);

            await recents.Received(1).RemoveAsync(missing, Arg.Any<CancellationToken>());
            await recents.DidNotReceive().RemoveAsync(existing, Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(existing);
        }
    }

    [Fact]
    public async Task RemoveMissingRecentsCommand_AllExist_NoRemovalCalled()
    {
        var f1 = Path.Combine(Path.GetTempPath(), $"foliant-rmr-{Guid.NewGuid():N}.pdf");
        var f2 = Path.Combine(Path.GetTempPath(), $"foliant-rmr-{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(f1, string.Empty);
        await File.WriteAllTextAsync(f2, string.Empty);
        try
        {
            var recents = Substitute.For<IRecentsService>();
            recents.GetAsync(Arg.Any<CancellationToken>()).Returns(new[] { f1, f2 });
            var vm = CreateVm(recents);

            await vm.RemoveMissingRecentsCommand.ExecuteAsync(null);

            await recents.DidNotReceive().RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(f1);
            File.Delete(f2);
        }
    }

    [Fact]
    public async Task RemoveMissingRecentsCommand_EmptyMru_NoOp()
    {
        var recents = Substitute.For<IRecentsService>();
        recents.GetAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());
        var vm = CreateVm(recents);

        await vm.RemoveMissingRecentsCommand.ExecuteAsync(null);

        await recents.DidNotReceive().RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ───── Dedupe-on-open (S11/W) ─────

    [Fact]
    public async Task OpenDocumentFromPath_AlreadyOpen_FocusesExistingTab()
    {
        var recents = Substitute.For<IRecentsService>();
        recents.GetAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());
        var vm = CreateVm(recents);
        var t1 = MakeTabStub("/tmp/a.pdf");
        var t2 = MakeTabStub("/tmp/b.pdf");
        vm.Tabs.Add(t1);
        vm.Tabs.Add(t2);
        vm.SelectedTab = t2;

        await vm.OpenDocumentFromPathAsync("/tmp/a.pdf", default);

        // Существующий таб сфокусирован, новый не создан.
        vm.SelectedTab.Should().BeSameAs(t1);
        vm.Tabs.Should().HaveCount(2);
        // MRU всё равно обновился — пользователь явно «открывал» этот файл.
        await recents.Received(1).AddAsync("/tmp/a.pdf", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenDocumentFromPath_AlreadyOpen_CaseInsensitive()
    {
        var recents = Substitute.For<IRecentsService>();
        recents.GetAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());
        var vm = CreateVm(recents);
        var t = MakeTabStub("/tmp/A.PDF");
        vm.Tabs.Add(t);

        await vm.OpenDocumentFromPathAsync("/tmp/a.pdf", default);

        vm.SelectedTab.Should().BeSameAs(t);
        vm.Tabs.Should().HaveCount(1);
    }

    // ───── HasRecentFiles (S11/U) ─────

    [Fact]
    public void HasRecentFiles_FollowsCollection()
    {
        var vm = CreateVm();

        vm.HasRecentFiles.Should().BeFalse();

        vm.RecentFiles.Add("a.pdf");
        vm.HasRecentFiles.Should().BeTrue();

        vm.RecentFiles.Add("b.pdf");
        vm.HasRecentFiles.Should().BeTrue();

        vm.RecentFiles.Clear();
        vm.HasRecentFiles.Should().BeFalse();
    }

    [Fact]
    public void HasRecentFiles_FiresPropertyChanged_OnAddRemove()
    {
        var vm = CreateVm();
        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        vm.RecentFiles.Add("x.pdf");
        fired.Should().Contain(nameof(MainViewModel.HasRecentFiles));

        fired.Clear();
        vm.RecentFiles.Clear();
        fired.Should().Contain(nameof(MainViewModel.HasRecentFiles));
    }

    // ───── License status (S13/E) ─────

    [Fact]
    public async Task LicenseStatus_NoLicenseManager_StaysNull()
    {
        var vm = CreateVm(licenseManager: null);

        await vm.InitializeAsync(default);

        vm.LicenseStatus.Should().BeNull();
    }

    [Fact]
    public async Task LicenseStatus_AfterInitialize_ReflectsManagerVerdict()
    {
        var lm = Substitute.For<ILicenseManager>();
        var lic = new License("alice", "Pro", DateTimeOffset.UtcNow.AddDays(180), ["editor"]);
        lm.CurrentAsync(Arg.Any<CancellationToken>())
          .Returns(LicenseValidationResult.Valid(lic));
        var vm = CreateVm(licenseManager: lm);

        await vm.InitializeAsync(default);

        vm.LicenseStatus!.Status.Should().Be(LicenseStatus.Valid);
        vm.LicenseStatus.License!.User.Should().Be("alice");
    }

    [Fact]
    public async Task RefreshLicenseStatus_ReReads_FromManager()
    {
        var lm = Substitute.For<ILicenseManager>();
        var first = LicenseValidationResult.Missing;
        var second = LicenseValidationResult.Valid(new License("u", "Pro", DateTimeOffset.UtcNow.AddYears(1), []));
        var sequence = new Queue<LicenseValidationResult>([first, second]);
        lm.CurrentAsync(Arg.Any<CancellationToken>())
          .Returns(_ => sequence.Dequeue());
        var vm = CreateVm(licenseManager: lm);

        await vm.InitializeAsync(default);
        vm.LicenseStatus.Should().BeSameAs(first);

        await vm.RefreshLicenseStatusCommand.ExecuteAsync(null);
        vm.LicenseStatus.Should().BeSameAs(second);
    }

    [Fact]
    public async Task LicenseStatus_ManagerThrows_FallsBackToMissing_DoesNotPropagate()
    {
        var lm = Substitute.For<ILicenseManager>();
        lm.CurrentAsync(Arg.Any<CancellationToken>())
          .Returns(Task.FromException<LicenseValidationResult>(new IOException("disk")));
        var vm = CreateVm(licenseManager: lm);

        var act = async () => await vm.InitializeAsync(default);

        await act.Should().NotThrowAsync();
        vm.LicenseStatus!.Status.Should().Be(LicenseStatus.Missing);
    }

    // ───── CloseAllTabs (S11/Q) ─────

    [Fact]
    public async Task CloseAllTabsCommand_RemovesAllAndClearsSelection()
    {
        var vm = CreateVm();
        vm.Tabs.Add(MakeTabStub());
        vm.Tabs.Add(MakeTabStub());
        vm.Tabs.Add(MakeTabStub());
        vm.SelectedTab = vm.Tabs[1];

        await vm.CloseAllTabsCommand.ExecuteAsync(null);

        vm.Tabs.Should().BeEmpty();
        vm.SelectedTab.Should().BeNull();
        vm.HasOpenTab.Should().BeFalse();
    }

    [Fact]
    public async Task CloseAllTabsCommand_NoTabs_NoOp()
    {
        var vm = CreateVm();

        var act = async () => await vm.CloseAllTabsCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync();
        vm.Tabs.Should().BeEmpty();
    }

    private static DocumentTabViewModel MakeTabStub(string filePath = "/tmp/x.pdf")
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
        return new DocumentTabViewModel(doc, filePath, search, ann, bm, NullLogger<DocumentTabViewModel>.Instance);
    }
}
