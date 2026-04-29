using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Application.Settings;
using NSubstitute;
using Xunit;

namespace Foliant.ViewModels.Tests;

public sealed class SettingsViewModelTests
{
    private static SettingsViewModel CreateVm(
        AppSettings? initial = null,
        ISettingsService? settings = null,
        ILocalizationService? localization = null)
    {
        settings ??= Substitute.For<ISettingsService>();
        settings.Current.Returns(initial ?? AppSettings.Default);

        localization ??= Substitute.For<ILocalizationService>();
        localization.CurrentCulture.Returns("ru");

        return new SettingsViewModel(settings, localization);
    }

    [Fact]
    public void Constructor_LoadsFromCurrentSettings()
    {
        var initial = AppSettings.Default with
        {
            Theme = "Dark",
            Language = "en",
            Cache = new CacheSettings { DiskLimitBytes = 2L * 1024 * 1024 * 1024, ClearOnExit = true },
        };

        var vm = CreateVm(initial);

        vm.SelectedTheme.Should().Be("Dark");
        vm.SelectedLanguage.Should().Be("en");
        vm.DiskCacheLimitGb.Should().BeApproximately(2.0, 0.001);
        vm.ClearCacheOnExit.Should().BeTrue();
    }

    [Fact]
    public async Task SaveCommand_PersistsThroughSettingsService()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(AppSettings.Default);
        var localization = Substitute.For<ILocalizationService>();
        localization.CurrentCulture.Returns("ru");
        var vm = CreateVm(settings: settings, localization: localization);
        vm.SelectedTheme = "HighContrast";

        await vm.SaveCommand.ExecuteAsync(null);

        await settings.Received(1).SaveAsync(
            Arg.Is<AppSettings>(s => s.Theme == "HighContrast"),
            Arg.Any<CancellationToken>());
        vm.IsSaved.Should().BeTrue();
    }

    [Fact]
    public async Task SaveCommand_LanguageChanged_CallsLocalizationSetCulture()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(AppSettings.Default with { Language = "ru" });
        var localization = Substitute.For<ILocalizationService>();
        localization.CurrentCulture.Returns("ru");
        var vm = CreateVm(settings: settings, localization: localization);
        vm.SelectedLanguage = "en";

        await vm.SaveCommand.ExecuteAsync(null);

        localization.Received(1).SetCulture("en");
    }

    [Fact]
    public async Task SaveCommand_LanguageUnchanged_DoesNotCallSetCulture()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(AppSettings.Default with { Language = "ru" });
        var localization = Substitute.For<ILocalizationService>();
        localization.CurrentCulture.Returns("ru");
        var vm = CreateVm(settings: settings, localization: localization);
        // SelectedLanguage стартует с "ru"; не меняем

        await vm.SaveCommand.ExecuteAsync(null);

        localization.DidNotReceive().SetCulture(Arg.Any<string>());
    }

    [Fact]
    public void AvailableThemes_ContainsExpectedSet()
    {
        var vm = CreateVm();

        vm.AvailableThemes.Should().BeEquivalentTo(["Auto", "Light", "Dark", "HighContrast"]);
    }

    [Fact]
    public void AvailableLanguages_ContainsRuAndEn()
    {
        var vm = CreateVm();

        vm.AvailableLanguages.Should().BeEquivalentTo(["ru", "en"]);
    }
}
