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

    // ───── IsSaved auto-reset (S9/D) ─────

    [Fact]
    public async Task IsSaved_ResetsToFalse_WhenThemeChangedAfterSave()
    {
        var vm = CreateVm();
        await vm.SaveCommand.ExecuteAsync(null);
        vm.IsSaved.Should().BeTrue();

        vm.SelectedTheme = "Dark";

        vm.IsSaved.Should().BeFalse();
    }

    [Fact]
    public async Task IsSaved_ResetsToFalse_WhenLanguageChangedAfterSave()
    {
        var vm = CreateVm();
        await vm.SaveCommand.ExecuteAsync(null);

        vm.SelectedLanguage = "en";

        vm.IsSaved.Should().BeFalse();
    }

    [Fact]
    public async Task IsSaved_ResetsToFalse_WhenDiskLimitChangedAfterSave()
    {
        var vm = CreateVm();
        await vm.SaveCommand.ExecuteAsync(null);

        vm.DiskCacheLimitGb = 10.0;

        vm.IsSaved.Should().BeFalse();
    }

    [Fact]
    public async Task IsSaved_ResetsToFalse_WhenClearOnExitChangedAfterSave()
    {
        var vm = CreateVm();
        await vm.SaveCommand.ExecuteAsync(null);

        vm.ClearCacheOnExit = !vm.ClearCacheOnExit;

        vm.IsSaved.Should().BeFalse();
    }

    // ───── ResetToDefaults (S9/D) ─────

    [Fact]
    public void ResetToDefaultsCommand_RestoresDefaultValues()
    {
        var custom = AppSettings.Default with
        {
            Theme = "Dark",
            Language = "en",
            Cache = new CacheSettings { DiskLimitBytes = 10L * 1024 * 1024 * 1024, ClearOnExit = true },
        };
        var vm = CreateVm(custom);

        vm.ResetToDefaultsCommand.Execute(null);

        vm.SelectedTheme.Should().Be(AppSettings.Default.Theme);
        vm.SelectedLanguage.Should().Be(AppSettings.Default.Language);
        vm.DiskCacheLimitGb.Should().BeApproximately(
            AppSettings.Default.Cache.DiskLimitBytes / (1024.0 * 1024 * 1024), 0.001);
        vm.ClearCacheOnExit.Should().Be(AppSettings.Default.Cache.ClearOnExit);
    }

    [Fact]
    public async Task ResetToDefaultsCommand_AfterSave_SetIsSavedFalse()
    {
        var vm = CreateVm(AppSettings.Default with { Theme = "Dark" });
        await vm.SaveCommand.ExecuteAsync(null);
        vm.IsSaved.Should().BeTrue();

        vm.ResetToDefaultsCommand.Execute(null);

        vm.IsSaved.Should().BeFalse();
    }

    [Fact]
    public void ResetToDefaultsCommand_DoesNotCallSaveOnService()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(AppSettings.Default with { Theme = "Dark" });
        var vm = CreateVm(settings: settings);

        vm.ResetToDefaultsCommand.Execute(null);

        settings.DidNotReceive().SaveAsync(Arg.Any<AppSettings>(), Arg.Any<CancellationToken>());
    }
}
