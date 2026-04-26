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
    private static MainViewModel CreateVm(IRecentsService? recents = null, ISettingsService? settings = null)
    {
        var useCase = new OpenDocumentUseCase([], NullLogger<OpenDocumentUseCase>.Instance);
        Func<IDocument, string, DocumentTabViewModel> factory = (_, _) => throw new NotSupportedException();

        recents ??= Substitute.For<IRecentsService>();
        recents.GetAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());

        settings ??= Substitute.For<ISettingsService>();
        settings.Current.Returns(AppSettings.Default);

        return new MainViewModel(useCase, factory, recents, settings, NullLogger<MainViewModel>.Instance);
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
}
